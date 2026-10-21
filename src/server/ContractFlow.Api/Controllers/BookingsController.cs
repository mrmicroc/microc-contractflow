using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController(
    AppDbContext db, PadService pad, ICurrentUser currentUser, IFileStorage storage)
    : ControllerBase
{
    // HashSet (not arrays): Contains() on these must bind to the instance method
    // so EF translates it to SQL — arrays bind to MemoryExtensions.Contains(ReadOnlySpan)
    // which the query funcletizer can't compile.
    private static readonly HashSet<BookingStatus> Terminal =
        [BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.Rescheduled];

    // Pre-commitment statuses never touch the PAD ledger (design §4.3).
    private static readonly HashSet<BookingStatus> PreCommitment =
        [BookingStatus.Forecast, BookingStatus.Tentative, BookingStatus.Requested];

    /// <summary>
    /// Lists bookings, newest-visible window via from/to. Admin/PM/Finance see
    /// everything; a Resource user sees only their own bookings (§10).
    /// </summary>
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? projectId, [FromQuery] Guid? resourceId, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(ct);
        var query = db.Bookings
            .Include(b => b.Project).ThenInclude(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(b => b.Resource)
            .Include(b => b.WorkPackage)
            .AsQueryable();

        if (me!.Role == AppRole.Resource)
            query = query.Where(b => b.Resource.UserId == me.Id);
        if (from.HasValue) query = query.Where(b => b.Date >= from.Value);
        if (to.HasValue) query = query.Where(b => b.Date <= to.Value);
        if (projectId.HasValue) query = query.Where(b => b.ProjectId == projectId.Value);
        if (resourceId.HasValue) query = query.Where(b => b.ResourceId == resourceId.Value);

        var bookings = await query.OrderBy(b => b.Date).ThenBy(b => b.CreatedUtc).ToListAsync(ct);
        var result = await WithWarningsAndPadAsync(bookings, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        var me = await currentUser.GetAsync(ct);
        if (me!.Role == AppRole.Resource && booking.Resource.UserId != me.Id)
            return ForbidResult();

        var dto = (await WithWarningsAndPadAsync([booking], ct)).Single();
        return Ok(dto);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Create(BookingRequest req, CancellationToken ct)
    {
        if (!PreCommitment.Contains(req.Status))
            return BadRequest(new { error = "New bookings start as forecast, tentative or requested — confirm via the confirm action." });
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });
        if (!await db.Resources.AnyAsync(r => r.Id == req.ResourceId && r.IsActive, ct))
            return BadRequest(new { error = "Unknown or inactive resource." });
        if (req.WorkPackageId.HasValue && !await db.WorkPackages.AnyAsync(
                l => l.Id == req.WorkPackageId.Value && l.ProjectId == req.ProjectId, ct))
            return BadRequest(new { error = "WorkPackage does not belong to the project." });

        var me = await currentUser.GetAsync(ct);
        var booking = new Booking
        {
            ProjectId = req.ProjectId,
            ResourceId = req.ResourceId,
            WorkPackageId = req.WorkPackageId,
            Date = req.Date,
            PlannedHours = req.PlannedHours,
            ActivityRef = req.ActivityRef,
            WorkDescription = req.WorkDescription,
            CustomerContact = req.CustomerContact,
            EvidenceRef = req.EvidenceRef,
            InstructedBy = req.InstructedBy,
            InstructedAtUtc = req.InstructedAtUtc,
            Status = req.Status,
            CreatedByUserId = me?.Id,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);

        var created = await LoadAsync(booking.Id, ct);
        var dto = (await WithWarningsAndPadAsync([created!], ct)).Single();
        return CreatedAtAction(nameof(Get), new { id = booking.Id }, dto);
    }

    /// <summary>
    /// Edits planning fields. A committed (Confirmed+) booking can no longer
    /// change date/resource/hours — re-date goes through reschedule, resource
    /// and hours changes need a cancel + rebook so PAD stays honest. Omitted
    /// planned hours keep the current value.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Update(Guid id, BookingUpdateRequest req, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (Terminal.Contains(booking.Status))
            return BadRequest(new { error = "Terminal bookings cannot be edited." });

        var committed = !PreCommitment.Contains(booking.Status);
        if (committed && (req.Date.HasValue && req.Date != booking.Date
            || req.ResourceId.HasValue && req.ResourceId != booking.ResourceId
            || req.PlannedHours.HasValue && req.PlannedHours != booking.PlannedHours))
            return BadRequest(new
            {
                error = "Date, resource and planned hours of a committed booking can only " +
                        "change via reschedule or cancel+rebook — PAD consequences must be explicit.",
            });

        if (req.Date.HasValue) booking.Date = req.Date.Value;
        if (req.ResourceId.HasValue)
        {
            if (!await db.Resources.AnyAsync(r => r.Id == req.ResourceId.Value && r.IsActive, ct))
                return BadRequest(new { error = "Unknown or inactive resource." });
            booking.ResourceId = req.ResourceId.Value;
        }
        if (req.WorkPackageId.HasValue && !await db.WorkPackages.AnyAsync(
                l => l.Id == req.WorkPackageId.Value && l.ProjectId == booking.ProjectId, ct))
            return BadRequest(new { error = "WorkPackage does not belong to the project." });
        booking.WorkPackageId = req.WorkPackageId;
        if (req.PlannedHours.HasValue) booking.PlannedHours = req.PlannedHours.Value;
        booking.ActivityRef = req.ActivityRef;
        booking.WorkDescription = req.WorkDescription;
        booking.CustomerContact = req.CustomerContact;
        booking.EvidenceRef = req.EvidenceRef;
        booking.InstructedBy = req.InstructedBy;
        booking.InstructedAtUtc = req.InstructedAtUtc;

        // Non-committed bookings may move freely between the pre-commitment statuses.
        if (req.Status.HasValue)
        {
            if (!PreCommitment.Contains(req.Status.Value))
                return BadRequest(new { error = "Use the status actions (confirm/cancel/…)." });
            if (committed)
                return BadRequest(new { error = "Committed bookings cannot return to planning statuses." });
            booking.Status = req.Status.Value;
        }

        await db.SaveChangesAsync(ct);
        var dto = (await WithWarningsAndPadAsync([booking], ct)).Single();
        return Ok(dto);
    }

    /// <summary>Confirm → Booked Day. Reserves PAD; 409 when free PAD is short.</summary>
    [HttpPost("{id:guid}/confirm")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (!PreCommitment.Contains(booking.Status))
            return BadRequest(new { error = $"Cannot confirm a booking in status {booking.Status}." });

        var me = await currentUser.GetAsync(ct);
        PadService.CommitResult result;
        try
        {
            // Commit + status change in one transaction — a concurrent second
            // confirm hits the one-commit-per-booking index instead.
            result = await pad.InTransactionAsync(async () =>
            {
                // Work at a package outside the contract's scope is additional
                // — confirmed without touching the PAD ledger (WorkPackageRules).
                var r = await WorkPackageRules.IsOutsideContractAsync(db, booking.WorkPackageId, ct)
                    ? new PadService.CommitResult(true, 0m, 0m, MonthClosed: false)
                    : await pad.TryCommitAsync(booking, me?.Id, ct);
                if (r.Ok)
                {
                    booking.Status = BookingStatus.Confirmed;
                    await db.SaveChangesAsync(ct);
                }
                return r;
            }, ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "concurrentUpdate", message = "The booking was confirmed concurrently." });
        }
        if (result.MonthClosed)
            return Conflict(new { error = "monthClosed", message = "The booking month is closed." });
        if (!result.Ok)
            return Conflict(new
            {
                error = "insufficientPad",
                freePad = result.FreeAfter,
                neededPad = result.Amount,
                message = "Insufficient free PAD — prepare an Additional PAD request.",
            });

        return Ok(await SingleDtoAsync(booking.Id, ct));
    }

    /// <summary>Resource acknowledges the assignment (flag — PAD stays committed).</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Confirmed)
            return BadRequest(new { error = "Only confirmed bookings can be acknowledged." });
        if (!await IsAdminOrManagerOrOwnerAsync(booking, ct)) return ForbidResult();

        booking.AcknowledgedUtc = DateTime.UtcNow;
        booking.Status = BookingStatus.Acknowledged;
        await db.SaveChangesAsync(ct);
        return Ok(await SingleDtoAsync(booking.Id, ct));
    }

    [HttpPost("{id:guid}/start")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.Acknowledged))
            return BadRequest(new { error = "Only confirmed/acknowledged bookings can start." });
        if (!await IsAdminOrManagerOrOwnerAsync(booking, ct)) return ForbidResult();
        if (await MonthClosedAsync(booking, ct)) return MonthClosedError();

        booking.Status = BookingStatus.InProgress;
        await db.SaveChangesAsync(ct);
        return Ok(await SingleDtoAsync(booking.Id, ct));
    }

    /// <summary>
    /// Complete → Utilise the open PAD commitment. MICROC-side only (§6.1:
    /// utilisation follows MICROC approval) and never ahead of the booked
    /// date. Optional <c>actualHours</c> beyond the commitment allocate the
    /// overrun per §4.3 ①–④; the source split is returned.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Complete(
        Guid id, [FromBody] CompleteRequest? req, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.Acknowledged
            or BookingStatus.InProgress))
            return BadRequest(new { error = "Only active bookings can complete." });
        if (booking.Date > pad.Today)
            return BadRequest(new { error = "futureBooking", message = "A booking cannot complete before its date." });
        if (await MonthClosedAsync(booking, ct)) return MonthClosedError();

        var me = await currentUser.GetAsync(ct);
        var (utilised, overrun) = await pad.InTransactionAsync(async () =>
        {
            var used = await pad.SettleCommitmentAsync(
                booking, PadTransactionType.Utilise, me?.Id, "Completed booked day", ct);

            OverrunSplit? split = null;
            if (used > 0 && req?.ActualHours is { } actual)
            {
                var terms = await pad.TermsForAsync(booking.ProjectId, booking.Date, ct);
                var actualPad = PadMath.CommitAmount(actual,
                    terms?.PadHoursPerDay ?? 8m, terms?.MinPadPerBookedDay ?? 1m);
                if (actualPad > used)
                    split = await pad.AllocateOverrunAsync(booking, actualPad - used, me?.Id, ct);
            }
            booking.Status = BookingStatus.Completed;
            await db.SaveChangesAsync(ct);
            return (used, split);
        }, ct);

        var dto = await SingleDtoAsync(booking.Id, ct);
        return Ok(new { booking = dto, padUtilised = utilised + (overrun?.Total ?? 0m), overrun });
    }

    /// <summary>
    /// Cancel. Timely (≥ contract deadline, default 4 business days before the
    /// booked date) → commitment released. Late → PAD consumed per §4.3.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (Terminal.Contains(booking.Status))
            return BadRequest(new { error = "Terminal bookings cannot be cancelled." });
        if (await MonthClosedAsync(booking, ct)) return MonthClosedError();

        var me = await currentUser.GetAsync(ct);
        var (effect, amount) = await pad.InTransactionAsync(async () =>
        {
            var r = await CancelPadEffectAsync(booking, me?.Id, ct);
            booking.Status = BookingStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            return r;
        }, ct);

        var dto = await SingleDtoAsync(booking.Id, ct);
        return Ok(new { booking = dto, padEffect = effect, padAmount = amount });
    }

    /// <summary>
    /// Reschedule = cancel-with-PAD-consequences on the original + a new booking
    /// (which then tries to confirm). Never a silent move (§4.3, §7.1). A
    /// pre-commitment original re-plans as-is: the replacement keeps its
    /// status and the ledger is never touched.
    /// </summary>
    [HttpPost("{id:guid}/reschedule")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Reschedule(Guid id, RescheduleRequest req, CancellationToken ct)
    {
        var booking = await LoadAsync(id, ct);
        if (booking is null) return NotFound();
        if (Terminal.Contains(booking.Status))
            return BadRequest(new { error = "Terminal bookings cannot be rescheduled." });
        if (req.Date == booking.Date)
            return BadRequest(new { error = "Reschedule needs a different date." });
        if (await MonthClosedAsync(booking, ct)) return MonthClosedError();

        var me = await currentUser.GetAsync(ct);
        var wasCommitted = !PreCommitment.Contains(booking.Status);
        var (effect, amount, replacement, commit) = await pad.InTransactionAsync(async () =>
        {
            var (fx, amt) = await CancelPadEffectAsync(booking, me?.Id, ct);

            var copy = new Booking
            {
                ProjectId = booking.ProjectId,
                ResourceId = booking.ResourceId,
                WorkPackageId = booking.WorkPackageId,
                Date = req.Date,
                PlannedHours = req.PlannedHours ?? booking.PlannedHours,
                ActivityRef = booking.ActivityRef,
                WorkDescription = booking.WorkDescription,
                CustomerContact = booking.CustomerContact,
                EvidenceRef = booking.EvidenceRef,
                InstructedBy = booking.InstructedBy,
                InstructedAtUtc = booking.InstructedAtUtc,
                Status = wasCommitted ? BookingStatus.Requested : booking.Status,
                ReplacesBookingId = booking.Id,
                CreatedByUserId = me?.Id,
            };
            db.Bookings.Add(copy);
            booking.Status = BookingStatus.Rescheduled;
            await db.SaveChangesAsync(ct);
            booking.RescheduledToBookingId = copy.Id;

            // A confirmed original was a customer instruction — its replacement
            // tries to confirm immediately. Planning items stay planning items.
            PadService.CommitResult? c = null;
            if (wasCommitted)
            {
                c = await WorkPackageRules.IsOutsideContractAsync(db, copy.WorkPackageId, ct)
                    ? new PadService.CommitResult(true, 0m, 0m, MonthClosed: false)
                    : await pad.TryCommitAsync(copy, me?.Id, ct);
                if (c.Ok) copy.Status = BookingStatus.Confirmed;
            }
            await db.SaveChangesAsync(ct);
            return (fx, amt, copy, c);
        }, ct);

        var oldDto = await SingleDtoAsync(booking.Id, ct);
        var newDto = await SingleDtoAsync(replacement.Id, ct);
        var confirmed = commit?.Ok == true;
        return Ok(new
        {
            original = oldDto,
            replacement = newDto,
            padEffect = effect,
            padAmount = amount,
            replacementConfirmed = confirmed,
            insufficientPad = commit is { Ok: false, MonthClosed: false },
            replacementMonthClosed = commit?.MonthClosed == true,
            freePad = commit is { Ok: false } ? (decimal?)commit.FreeAfter : null,
        });
    }

    /// <summary>
    /// Hard delete — only pre-commitment bookings (they never touched the PAD
    /// ledger). Committed bookings must be cancelled: the audit trail stays.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var booking = await db.Bookings.FindAsync([id], ct);
        if (booking is null) return NotFound();
        if (!PreCommitment.Contains(booking.Status))
            return Conflict(new
            {
                error = "committedBooking",
                message = "Committed bookings have PAD consequences — cancel instead of deleting.",
            });

        var docs = await db.Documents
            .Where(d => d.OwnerType == DocumentOwnerType.Booking && d.OwnerId == id)
            .ToListAsync(ct);
        db.Documents.RemoveRange(docs);
        db.Bookings.Remove(booking);
        await db.SaveChangesAsync(ct);
        // Stored files go only once the rows are gone — a failed save must
        // not leave document rows pointing at deleted files.
        foreach (var d in docs) await storage.DeleteAsync(d.StorageKey, ct);
        return NoContent();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private Task<Booking?> LoadAsync(Guid id, CancellationToken ct) =>
        db.Bookings
            .Include(b => b.Project).ThenInclude(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(b => b.Resource)
            .Include(b => b.WorkPackage)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    private async Task<bool> IsAdminOrManagerOrOwnerAsync(Booking b, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(ct);
        if (me is null) return false;
        if (me.Role is AppRole.Administrator or AppRole.ProjectManager) return true;
        return me.Role == AppRole.Resource && b.Resource.UserId == me.Id;
    }

    private Task<bool> MonthClosedAsync(Booking b, CancellationToken ct) =>
        pad.MonthIsClosedAsync(b.ProjectId, b.Date.Year, b.Date.Month, ct);

    private ConflictObjectResult MonthClosedError() =>
        Conflict(new { error = "monthClosed", message = "The booking month is closed." });

    private IActionResult ForbidResult() =>
        new ObjectResult(new { error = "Forbidden." }) { StatusCode = 403 };

    /// <summary>
    /// Applies the versioned cancellation policy (§4.3): the steps ladder —
    /// or the legacy binary deadline when no steps — resolves the charge.
    /// An open PAD commitment materialises hour charges as utilised PAD
    /// (remainder released) — a full charge consumes the whole commitment, so
    /// a late cancel never costs less than the 1.00 PAD minimum. Without a
    /// commitment the charge rides the booking into the month's invoice. A
    /// fixed fee always rides the booking, alongside any hour charge.
    /// </summary>
    private async Task<(string effect, decimal amount)> CancelPadEffectAsync(
        Booking booking, Guid? userId, CancellationToken ct)
    {
        if (PreCommitment.Contains(booking.Status))
            return ("none", 0m);

        var terms = await pad.TermsForAsync(booking.ProjectId, booking.Date, ct);
        var daysBefore = BusinessDays.CountBusinessDays(
            pad.Today, booking.Date, terms?.HolidayCalendar ?? "SE");
        var charge = CancellationMath.Resolve(terms?.CancellationSteps, daysBefore,
            booking.PlannedHours, terms?.CancellationDeadlineBusinessDays ?? 4);

        // The branch follows the ledger, not today's terms — a commitment made
        // under PAD terms settles as PAD even if later terms dropped PAD.
        var open = await pad.OpenCommitmentAsync(booking.Id, ct);
        if (open > 0m)
        {
            if (charge.Amount is > 0m)
                booking.CancellationChargeAmount = charge.Amount;

            var padHours = terms?.PadHoursPerDay is > 0m ? terms.PadHoursPerDay : 8m;
            var utilisePad = charge.Full
                ? open
                : charge.Hours is > 0m ? Math.Min(open, charge.Hours.Value / padHours) : 0m;
            var (used, released) = await pad.SettlePartialAsync(booking, utilisePad, userId, ct);
            return (used > 0m && released > 0m ? "partial"
                : used > 0m ? "utilised" : "released", used);
        }

        // No commitment (non-PAD contract) — the charge rides the booking into
        // the month's invoice.
        booking.CancellationChargeHours = charge.Hours;
        booking.CancellationChargeAmount = charge.Amount;
        booking.CancellationChargeFull = charge.Full;
        // Day-rate contracts charge a share of the day: a whole day when the
        // booking is charged in full, else the rung's fraction (if it set one).
        booking.CancellationChargeDayFraction = terms?.TimeBilling == TimeBilling.DailyRate
            ? charge.Full ? 1m : charge.DayFraction
            : null;
        return (charge.IsFree ? "released" : "charged",
            charge.Amount ?? booking.CancellationChargeDayFraction ?? charge.Hours ?? 0m);
    }

    private async Task<BookingDto> SingleDtoAsync(Guid id, CancellationToken ct) =>
        (await WithWarningsAndPadAsync([await LoadAsync(id, ct) ?? throw new InvalidOperationException("Booking vanished")], ct)).Single();

    /// <summary>
    /// Maps bookings to DTOs adding expected PAD (from the terms version in
    /// force on the booking date) and conflict warnings: double-booked resource
    /// and leave overlap (design §7.1). Resource callers never see customer
    /// pricing — expected PAD and cancellation charges are blanked (§10).
    /// </summary>
    private async Task<List<BookingDto>> WithWarningsAndPadAsync(
        List<Booking> live, CancellationToken ct)
    {
        if (live.Count == 0) return [];

        var me = await currentUser.GetAsync(ct);
        var hidePricing = me?.Role == AppRole.Resource;

        var contractIds = live.Select(b => b.Project.ContractId).Distinct().ToList();
        var allTerms = await db.ContractTerms
            .Where(t => contractIds.Contains(t.ContractId))
            .ToListAsync(ct);

        var resourceIds = live.Select(b => b.ResourceId).Distinct().ToList();
        var dates = live.Select(b => b.Date).Distinct().ToList();
        var siblings = await db.Bookings
            .Where(b => resourceIds.Contains(b.ResourceId) && dates.Contains(b.Date)
                && !Terminal.Contains(b.Status))
            .Select(b => new { b.Id, b.ResourceId, b.Date })
            .ToListAsync(ct);
        var minDate = dates.Min();
        var maxDate = dates.Max();
        var leaves = await db.ResourceLeaves
            .Where(l => resourceIds.Contains(l.ResourceId)
                && l.From <= maxDate && l.To >= minDate)
            .Select(l => new { l.ResourceId, l.From, l.To })
            .ToListAsync(ct);

        return live.Select(b =>
        {
            var terms = allTerms
                .Where(t => t.ContractId == b.Project.ContractId && t.EffectiveFrom <= b.Date)
                .OrderByDescending(t => t.EffectiveFrom).ThenByDescending(t => t.Version)
                .FirstOrDefault();
            decimal? expectedPad = terms?.PadEnabled == true && !hidePricing
                ? PadMath.CommitAmount(
                    b.PlannedHours, terms.PadHoursPerDay, terms.MinPadPerBookedDay)
                : null;

            var warnings = new List<string>();
            if (!Terminal.Contains(b.Status))
            {
                if (siblings.Any(s => s.Id != b.Id && s.ResourceId == b.ResourceId && s.Date == b.Date))
                    warnings.Add("doubleBooked");
                if (leaves.Any(l => l.ResourceId == b.ResourceId && b.Date >= l.From && b.Date <= l.To))
                    warnings.Add("onLeave");
            }

            return new BookingDto(
                b.Id, b.ProjectId, b.Project.Name, b.Project.Contract.Customer.Name,
                b.ResourceId, b.Resource.DisplayName, b.WorkPackageId, b.WorkPackage?.Name,
                b.Date, b.PlannedHours, b.ActivityRef, b.WorkDescription,
                b.CustomerContact, b.EvidenceRef, b.Status, expectedPad,
                b.AcknowledgedUtc,
                hidePricing ? null : b.CancellationChargeHours,
                hidePricing ? null : b.CancellationChargeAmount,
                b.ReplacesBookingId, b.RescheduledToBookingId,
                warnings.ToArray(),
                b.InstructedBy, b.InstructedAtUtc,
                hidePricing ? null : b.CancellationChargeDayFraction);
        }).ToList();
    }
}
