using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// The resource × work-package rate matrix (WorkPackageRate.ResourceId), the
/// project resource roster (ProjectResource) feeding rate selectors, the
/// NeedsReview lifecycle on inherited rates, and the work-package field on
/// unbooked time rows.
/// </summary>
public class ResourceRateMatrixTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);
    public record LineDto(string Kind, string? Date, string Description,
        string? Explanation, decimal Quantity, decimal UnitPrice, decimal Amount);
    public record InvoiceDto(Guid Id, string Status, decimal TotalAmount);
    public record InvoiceDetail(InvoiceDto Invoice, LineDto[] Lines);
    public record RateDto(Guid Id, string EffectiveFrom, Guid? ResourceId,
        string? ResourceName, bool NeedsReview, decimal? HourlyRate,
        decimal? SaturdayRate, decimal? MobilisationFee);
    public record MemberDto(Guid ResourceId, string ResourceName, int PendingReviews);
    public record ProjectDto(Guid Id, string Name);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient PM => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private async Task<Scaffold.Ids> GraphAsync(string tag, object terms)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        var res = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", terms);
        res.EnsureSuccessStatusCode();
        return ids;
    }

    private async Task<Guid> AddWorkPackageAsync(Guid projectId, string name)
    {
        var res = await Admin.PostAsJsonAsync("/api/work-packages", new { projectId, name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    private async Task<Guid> CreateResourceAsync(string name)
    {
        var res = await Admin.PostAsJsonAsync("/api/resources",
            new { displayName = name, type = "employee" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    private async Task AddMemberAsync(Guid projectId, Guid resourceId)
    {
        var res = await Admin.PostAsJsonAsync($"/api/projects/{projectId}/resources",
            new { resourceId });
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Membership without the inherited-rate side effect — for pure
    /// resolution tests that manage cells explicitly.</summary>
    private async Task SeedMembershipAsync(Guid projectId, Guid resourceId) =>
        await factory.SeedAsync(async db =>
        {
            db.ProjectResources.Add(new ProjectResource
            {
                ProjectId = projectId, ResourceId = resourceId,
            });
            await db.SaveChangesAsync();
        });

    private async Task<Guid> ConfirmedBookingAsync(Scaffold.Ids ids, Guid resourceId,
        string date, Guid? workPackageId)
    {
        var res = await Admin.PostAsJsonAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId, date,
            plannedHours = 8m, status = "requested", workPackageId,
        });
        res.EnsureSuccessStatusCode();
        var id = (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
        (await Admin.PostAsync($"/api/bookings/{id}/confirm", null))
            .EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>Posts entries for an arbitrary resource as admin, submits the
    /// containing ISO week for that resource and approves it.</summary>
    private async Task ApproveAsync(Guid resourceId, string monday, params object[] entries)
    {
        foreach (var e in entries)
            (await Admin.PostAsJsonAsync("/api/time-entries", e)).EnsureSuccessStatusCode();
        var d = DateOnly.Parse(monday);
        var dt = d.ToDateTime(TimeOnly.MinValue);
        var submit = await Admin.PostAsJsonAsync("/api/timesheets/submit", new
        {
            resourceId,
            year = System.Globalization.ISOWeek.GetYear(dt),
            isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(dt),
        });
        submit.EnsureSuccessStatusCode();
        var sheetId = (await submit.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
        (await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null))
            .EnsureSuccessStatusCode();
    }

    private async Task<InvoiceDetail> InvoiceAsync(Scaffold.Ids ids, int year, int month)
    {
        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year, month });
        gen.EnsureSuccessStatusCode();
        var inv = await gen.Content.ReadFromJsonAsync<InvoiceDto>();
        return (await Admin.GetFromJsonAsync<InvoiceDetail>($"/api/invoices/{inv!.Id}"))!;
    }

    // ── The matrix: 4 resources × 2 packages = 8 independent cells ──────────

    /// <summary>Four members × two packages, every cell a distinct rate —
    /// each booking prices at exactly its (resource, package) cell.</summary>
    [Fact]
    public async Task Matrix_FourByTwo_EightIndependentCells()
    {
        var ids = await GraphAsync("matrix-4x2", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp1 = await AddWorkPackageAsync(ids.ProjectId, "Package One");
        var wp2 = await AddWorkPackageAsync(ids.ProjectId, "Package Two");
        var res = new Guid[4];
        for (var i = 0; i < 4; i++)
        {
            res[i] = await CreateResourceAsync($"Matrix Res {i + 1}");
            await SeedMembershipAsync(ids.ProjectId, res[i]);
        }

        // Every cell gets its own rate (base 1100 → +100 per resource, +50 wp2).
        var rate = new decimal[4, 2];
        for (var i = 0; i < 4; i++)
            for (var w = 0; w < 2; w++)
            {
                rate[i, w] = 1100m + i * 100m + w * 50m;
                var post = await Admin.PostAsJsonAsync(
                    $"/api/work-packages/{(w == 0 ? wp1 : wp2)}/rates",
                    new
                    {
                        resourceId = res[i], effectiveFrom = "2029-01-01",
                        hourlyRate = rate[i, w],
                    });
                post.EnsureSuccessStatusCode();
            }

        // Each resource works Mon on wp1, Tue on wp2.
        for (var i = 0; i < 4; i++)
        {
            var b1 = await ConfirmedBookingAsync(ids, res[i], "2029-01-01", wp1);
            var b2 = await ConfirmedBookingAsync(ids, res[i], "2029-01-02", wp2);
            await ApproveAsync(res[i], "2029-01-01",
                new { resourceId = res[i], projectId = ids.ProjectId, bookingId = b1, date = "2029-01-01", actualHours = 8m },
                new { resourceId = res[i], projectId = ids.ProjectId, bookingId = b2, date = "2029-01-02", actualHours = 8m });
        }

        var detail = await InvoiceAsync(ids, 2029, 1);
        var got = detail.Lines
            .Where(l => l.Kind == "hours")
            .Select(l => l.UnitPrice).OrderBy(x => x).ToArray();
        var want = rate.Cast<decimal>().OrderBy(x => x).ToArray();
        Assert.Equal(want, got);
    }

    /// <summary>Resource cell wins over the package-wide row, which wins over
    /// the contract — the three-level fallback chain end to end.</summary>
    [Fact]
    public async Task Matrix_ResourceCell_BeatsPackageRow_BeatsContract()
    {
        var ids = await GraphAsync("matrix-prec", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Priced Package");
        var resA = await CreateResourceAsync("Res A");
        var resB = await CreateResourceAsync("Res B");
        await SeedMembershipAsync(ids.ProjectId, resA);
        await SeedMembershipAsync(ids.ProjectId, resB);

        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { effectiveFrom = "2029-01-01", hourlyRate = 500m }))
            .EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { resourceId = resA, effectiveFrom = "2029-01-01", hourlyRate = 900m }))
            .EnsureSuccessStatusCode();

        var bA = await ConfirmedBookingAsync(ids, resA, "2029-02-05", wp);
        var bB = await ConfirmedBookingAsync(ids, resB, "2029-02-05", wp);
        await ApproveAsync(resA, "2029-02-05",
            new { resourceId = resA, projectId = ids.ProjectId, bookingId = bA, date = "2029-02-05", actualHours = 8m });
        await ApproveAsync(resB, "2029-02-05",
            new { resourceId = resB, projectId = ids.ProjectId, bookingId = bB, date = "2029-02-05", actualHours = 8m });

        var detail = await InvoiceAsync(ids, 2029, 2);
        var prices = detail.Lines.Where(l => l.Kind == "hours")
            .Select(l => l.UnitPrice).OrderBy(x => x).ToArray();
        // resA at its 900 cell; resB falls to the package row's 500.
        Assert.Equal(new[] { 500m, 900m }, prices);
        Assert.Contains(detail.Lines, l => l.UnitPrice == 900m
            && l.Explanation!.Contains("resource rates"));
        Assert.Contains(detail.Lines, l => l.UnitPrice == 500m
            && l.Explanation!.Contains("package rates"));
    }

    /// <summary>A resource cell only setting HourlyRate still inherits the
    /// package row's SaturdayRate — the fallback is per field.</summary>
    [Fact]
    public async Task Matrix_CellFieldFallback_SaturdayHitsPackageRow()
    {
        var ids = await GraphAsync("matrix-field", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false,
            hourlyRate = 100m, saturdayRate = 200m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Weekend Package");
        var resA = await CreateResourceAsync("Sat Res");
        await SeedMembershipAsync(ids.ProjectId, resA);

        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { effectiveFrom = "2029-01-01", hourlyRate = 500m, saturdayRate = 700m }))
            .EnsureSuccessStatusCode();
        // The cell sets only the weekday rate.
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { resourceId = resA, effectiveFrom = "2029-01-01", hourlyRate = 900m }))
            .EnsureSuccessStatusCode();

        var fri = await ConfirmedBookingAsync(ids, resA, "2029-03-02", wp); // Fri
        var sat = await ConfirmedBookingAsync(ids, resA, "2029-03-03", wp); // Sat
        await ApproveAsync(resA, "2029-02-26",
            new { resourceId = resA, projectId = ids.ProjectId, bookingId = fri, date = "2029-03-02", actualHours = 8m },
            new { resourceId = resA, projectId = ids.ProjectId, bookingId = sat, date = "2029-03-03", actualHours = 8m });

        var detail = await InvoiceAsync(ids, 2029, 3);
        var prices = detail.Lines.Where(l => l.Kind == "hours")
            .Select(l => l.UnitPrice).OrderBy(x => x).ToArray();
        // Weekday 900 (cell), Saturday 700 (cell field null → package row).
        Assert.Equal(new[] { 700m, 900m }, prices);
    }

    /// <summary>Two effective-dated versions of a cell — the in-force version
    /// prices each date.</summary>
    [Fact]
    public async Task Matrix_CellVersions_ResolveByDate()
    {
        var ids = await GraphAsync("matrix-ver", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Versioned Package");
        var resA = await CreateResourceAsync("Ver Res");
        await SeedMembershipAsync(ids.ProjectId, resA);

        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { resourceId = resA, effectiveFrom = "2029-01-01", hourlyRate = 600m }))
            .EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { resourceId = resA, effectiveFrom = "2029-03-01", hourlyRate = 650m }))
            .EnsureSuccessStatusCode();

        var feb = await ConfirmedBookingAsync(ids, resA, "2029-02-05", wp);
        var mar = await ConfirmedBookingAsync(ids, resA, "2029-03-05", wp);
        await ApproveAsync(resA, "2029-02-05",
            new { resourceId = resA, projectId = ids.ProjectId, bookingId = feb, date = "2029-02-05", actualHours = 8m });
        await ApproveAsync(resA, "2029-03-05",
            new { resourceId = resA, projectId = ids.ProjectId, bookingId = mar, date = "2029-03-05", actualHours = 8m });

        var detail = await InvoiceAsync(ids, 2029, 3);
        Assert.Equal(650m, detail.Lines.Single(l => l.Kind == "hours").UnitPrice);
        var febDetail = await InvoiceAsync(ids, 2029, 2);
        Assert.Equal(600m, febDetail.Lines.Single(l => l.Kind == "hours").UnitPrice);
    }

    // ── Roster: membership, inheritance snapshot, review lifecycle ──────────

    /// <summary>Adding a resource snapshots the in-force resolved rate per
    /// package — the package-wide override where it exists, otherwise the
    /// contract — into NeedsReview cells; the review endpoint clears them.</summary>
    [Fact]
    public async Task Roster_Add_SnapshotsResolvedRates_AndReviewClears()
    {
        var ids = await GraphAsync("roster-add", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wpOver = await AddWorkPackageAsync(ids.ProjectId, "Override Pkg");
        var wpPlain = await AddWorkPackageAsync(ids.ProjectId, "Plain Pkg");
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wpOver}/rates",
            new { effectiveFrom = "2026-06-01", hourlyRate = 444m }))
            .EnsureSuccessStatusCode();

        var resA = await CreateResourceAsync("New Joiner");
        var add = await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/resources",
            new { resourceId = resA });
        add.EnsureSuccessStatusCode();
        var member = await add.Content.ReadFromJsonAsync<MemberDto>();
        Assert.Equal(2, member!.PendingReviews);

        var overRates = await Admin.GetFromJsonAsync<RateDto[]>(
            $"/api/work-packages/{wpOver}/rates");
        var overCell = overRates!.Single(r => r.ResourceId == resA);
        Assert.True(overCell.NeedsReview);
        Assert.Equal(444m, overCell.HourlyRate); // package row won over contract

        var plainRates = await Admin.GetFromJsonAsync<RateDto[]>(
            $"/api/work-packages/{wpPlain}/rates");
        var plainCell = plainRates!.Single(r => r.ResourceId == resA);
        Assert.True(plainCell.NeedsReview);
        Assert.Equal(100m, plainCell.HourlyRate); // contract snapshot

        // Review clears one cell → pending count drops.
        var rev = await Admin.PostAsync(
            $"/api/work-packages/{wpOver}/rates/{overCell.Id}/review", null);
        rev.EnsureSuccessStatusCode();
        var members = await Admin.GetFromJsonAsync<MemberDto[]>(
            $"/api/projects/{ids.ProjectId}/resources");
        Assert.Equal(1, members!.Single(m => m.ResourceId == resA).PendingReviews);
    }

    /// <summary>The matrix selector only accepts roster members — a resource
    /// not on the project cannot get a cell.</summary>
    [Fact]
    public async Task Roster_CellRequiresMembership()
    {
        var ids = await GraphAsync("roster-gate", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Gated Pkg");
        var outsider = await CreateResourceAsync("Not Staffed");

        var res = await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { resourceId = outsider, effectiveFrom = "2029-01-01", hourlyRate = 900m });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>Roster mutations reject duplicates and unknown resources.</summary>
    [Fact]
    public async Task Roster_Add_DuplicateAndUnknown_Rejected()
    {
        var ids = await GraphAsync("roster-dup", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var resA = await CreateResourceAsync("Member");
        await AddMemberAsync(ids.ProjectId, resA);

        var dup = await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/resources",
            new { resourceId = resA });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var unknown = await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/resources",
            new { resourceId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    /// <summary>Removing a member keeps their rate rows (billing history) but
    /// drops them from the roster listing.</summary>
    [Fact]
    public async Task Roster_Remove_KeepsRateHistory()
    {
        var ids = await GraphAsync("roster-del", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Hist Pkg");
        var resA = await CreateResourceAsync("Leaver");
        await AddMemberAsync(ids.ProjectId, resA);

        var del = await Admin.DeleteAsync($"/api/projects/{ids.ProjectId}/resources/{resA}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var members = await Admin.GetFromJsonAsync<MemberDto[]>(
            $"/api/projects/{ids.ProjectId}/resources");
        Assert.Empty(members!);
        var rates = await Admin.GetFromJsonAsync<RateDto[]>(
            $"/api/work-packages/{wp}/rates");
        Assert.Contains(rates!, r => r.ResourceId == resA); // history kept
    }

    /// <summary>Same scope+date cannot hold two versions.</summary>
    [Fact]
    public async Task Rates_DuplicateScopeAndDate_Conflicts()
    {
        var ids = await GraphAsync("rate-dup", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Dup Pkg");
        var resA = await CreateResourceAsync("Dup Res");
        await AddMemberAsync(ids.ProjectId, resA);

        var body = new { resourceId = resA, effectiveFrom = "2029-01-01", hourlyRate = 900m };
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates", body))
            .EnsureSuccessStatusCode();
        var dup = await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates", body);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        // But the same date on the package-wide scope is a different cell.
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { effectiveFrom = "2029-01-01", hourlyRate = 500m }))
            .EnsureSuccessStatusCode();
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    /// <summary>Resources may not manage the roster or review rates; PM may.</summary>
    [Fact]
    public async Task Roster_Roles_ResourceForbidden_PmAllowed()
    {
        var ids = await GraphAsync("roster-roles", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Role Pkg");
        var resA = await CreateResourceAsync("Role Res");

        var asResource = await Consultant.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/resources", new { resourceId = resA });
        Assert.Equal(HttpStatusCode.Forbidden, asResource.StatusCode);

        var asPm = await PM.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/resources", new { resourceId = resA });
        asPm.EnsureSuccessStatusCode();

        var cell = (await Admin.GetFromJsonAsync<RateDto[]>(
            $"/api/work-packages/{wp}/rates"))!.Single(r => r.ResourceId == resA);
        var reviewAsResource = await Consultant.PostAsync(
            $"/api/work-packages/{wp}/rates/{cell.Id}/review", null);
        Assert.Equal(HttpStatusCode.Forbidden, reviewAsResource.StatusCode);

        var reviewAsPm = await PM.PostAsync(
            $"/api/work-packages/{wp}/rates/{cell.Id}/review", null);
        reviewAsPm.EnsureSuccessStatusCode();
    }

    /// <summary>Only resource cells can be reviewed — the package-wide row
    /// rejects the review endpoint.</summary>
    [Fact]
    public async Task Rates_Review_PackageRow_Rejected()
    {
        var ids = await GraphAsync("rate-rev", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Rev Pkg");
        var post = await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { effectiveFrom = "2029-01-01", hourlyRate = 500m });
        var row = await post.Content.ReadFromJsonAsync<RateDto>();

        var res = await Admin.PostAsync(
            $"/api/work-packages/{wp}/rates/{row!.Id}/review", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── Work package on unbooked time rows ───────────────────────────────────

    /// <summary>An unbooked entry naming a package prices at that package's
    /// override (and its matrix cell for the reporting resource).</summary>
    [Fact]
    public async Task TimeEntry_UnbookedWorkPackage_PricesAtCell()
    {
        var ids = await GraphAsync("entry-wp", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Adhoc Pkg");
        var resA = await CreateResourceAsync("Adhoc Res");
        await SeedMembershipAsync(ids.ProjectId, resA);
        await factory.SeedAsync(async db =>
        {
            db.ProjectAccesses.Add(new ProjectAccess
            {
                ProjectId = ids.ProjectId, ResourceId = resA,
            });
            await db.SaveChangesAsync();
        });
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates",
            new { resourceId = resA, effectiveFrom = "2029-01-01", hourlyRate = 777m }))
            .EnsureSuccessStatusCode();

        // Unbooked row on the package — no booking at all.
        var post = await Admin.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId = resA, projectId = ids.ProjectId,
            workPackageId = wp, date = "2029-04-02", actualHours = 2m,
        });
        post.EnsureSuccessStatusCode();
        await ApproveAsync(resA, "2029-04-02");

        var detail = await InvoiceAsync(ids, 2029, 4);
        Assert.Equal(777m, detail.Lines.Single(l => l.Kind == "hours").UnitPrice);
    }

    /// <summary>workPackageId is forbidden on booked rows and must belong to
    /// the entry's project.</summary>
    [Fact]
    public async Task TimeEntry_WorkPackage_Validated()
    {
        var ids = await GraphAsync("entry-wp-val", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 100m,
        });
        var wp = await AddWorkPackageAsync(ids.ProjectId, "Val Pkg");
        var resA = await CreateResourceAsync("Val Res");
        var booking = await ConfirmedBookingAsync(ids, resA, "2029-04-03", wp);

        var both = await Admin.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId = resA, projectId = ids.ProjectId, bookingId = booking,
            workPackageId = wp, date = "2029-04-03", actualHours = 4m,
        });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        var foreign = await Admin.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId = resA, projectId = ids.ProjectId,
            workPackageId = Guid.NewGuid(), date = "2029-04-03", actualHours = 4m,
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }
}
