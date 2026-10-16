using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Site-rate overrides (LocationRate), project self-reporting access lists
/// (ProjectAccess + /api/projects/mine) and the per-entry billing mode —
/// the "multiple sites, different rates, different crews" feature.
/// </summary>
public class SiteAccessTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    private static readonly Guid LinkedId = TimeReportingTests.LinkedId;

    public record IdRef(Guid Id);
    public record LineDto(string Kind, string? Date, string Description,
        string? Explanation, decimal Quantity, decimal UnitPrice, decimal Amount);
    public record InvoiceDto(Guid Id, string Status, decimal TotalAmount);
    public record InvoiceDetail(InvoiceDto Invoice, LineDto[] Lines);
    public record RateDto(Guid Id, string EffectiveFrom, decimal? HourlyRate,
        decimal? DayRate, decimal? MobilisationFee);
    public record ProjectDto(Guid Id, string Name);
    public record AccessDto(Guid ResourceId, string ResourceName);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient PM => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    /// <summary>Graph + the user-linked resource + fresh contract terms
    /// (posted as a newer version so they win over the scaffold's PAD terms).</summary>
    private async Task<Scaffold.Ids> GraphAsync(string tag, object terms)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == LinkedId))
            {
                db.Resources.Add(new Resource
                {
                    Id = LinkedId, DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor, UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        var res = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", terms);
        res.EnsureSuccessStatusCode();
        return ids;
    }

    private async Task<Guid> AddLocationAsync(Guid projectId, string name)
    {
        var res = await Admin.PostAsJsonAsync("/api/locations",
            new { projectId, name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    /// <summary>Creates + confirms a booking for the linked resource
    /// (optionally at a site) and returns the booking id.</summary>
    private async Task<Guid> ConfirmedBookingAsync(Scaffold.Ids ids, string date,
        Guid? locationId = null)
    {
        var res = await Admin.PostAsJsonAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = LinkedId, date,
            plannedHours = 8m, status = "requested", locationId,
        });
        res.EnsureSuccessStatusCode();
        var id = (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
        (await Admin.PostAsync($"/api/bookings/{id}/confirm", null))
            .EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>Books the linked resource on the project (at a site), posts
    /// the entry on that booking as the resource, submits + approves the week.</summary>
    private async Task ApproveEntriesAsync(Scaffold.Ids ids, string monday,
        params object[] entries)
    {
        foreach (var e in entries)
            (await Consultant.PostAsJsonAsync("/api/time-entries", e)).EnsureSuccessStatusCode();
        var d = DateOnly.Parse(monday);
        var dt = d.ToDateTime(TimeOnly.MinValue);
        var submit = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year = System.Globalization.ISOWeek.GetYear(dt),
                  isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(dt) });
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

    // ── Location rates: CRUD + roles ───────────────────────────────────────

    [Fact]
    public async Task LocationRates_Crud_AdminOnly()
    {
        var ids = await GraphAsync("rate-crud", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        var loc = await AddLocationAsync(ids.ProjectId, "Site A");

        var denied = await Consultant.PostAsJsonAsync($"/api/locations/{loc}/rates",
            new { effectiveFrom = "2026-01-01", hourlyRate = 1500m });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var res = await Admin.PostAsJsonAsync($"/api/locations/{loc}/rates",
            new { effectiveFrom = "2026-01-01", hourlyRate = 1500m, notes = "difficult site" });
        res.EnsureSuccessStatusCode();
        var rate = await res.Content.ReadFromJsonAsync<RateDto>();
        Assert.Equal(1500m, rate!.HourlyRate);

        var list = await Admin.GetFromJsonAsync<RateDto[]>($"/api/locations/{loc}/rates");
        Assert.Single(list!);
        var other = await AddLocationAsync(ids.ProjectId, "Site B");
        Assert.Empty((await Admin.GetFromJsonAsync<RateDto[]>(
            $"/api/locations/{other}/rates"))!);

        var del = await Admin.DeleteAsync($"/api/locations/{loc}/rates/{rate.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Empty((await Admin.GetFromJsonAsync<RateDto[]>(
            $"/api/locations/{loc}/rates"))!);
    }

    // ── Billing: override wins, per-field fallback, versioning ─────────────

    /// <summary>Booked entries at a site with an hourly override price at the
    /// override; a site without overrides keeps the contract rate.</summary>
    [Fact]
    public async Task Billing_SiteOverride_Wins_PerFieldFallback()
    {
        var ids = await GraphAsync("site-ovr", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false,
            hourlyRate = 1000m, saturdayRate = 2000m,
        });
        var site = await AddLocationAsync(ids.ProjectId, "Override Site");
        // Override only the weekday rate — Saturday must fall back to contract.
        await Admin.PostAsJsonAsync($"/api/locations/{site}/rates",
            new { effectiveFrom = "2026-01-01", hourlyRate = 1500m });

        // Wed 2029-01-03 at override site; Sat 2029-01-06 same site.
        var wedId = await ConfirmedBookingAsync(ids, "2029-01-03", site);
        var satId = await ConfirmedBookingAsync(ids, "2029-01-06", site);

        await ApproveEntriesAsync(ids, "2029-01-01",
            new { projectId = ids.ProjectId, date = "2029-01-03", category = "work",
                  actualHours = 8m, bookingId = wedId },
            new { projectId = ids.ProjectId, date = "2029-01-06", category = "work",
                  actualHours = 8m, bookingId = satId });

        var detail = await InvoiceAsync(ids, 2029, 1);
        var wedLine = Assert.Single(detail.Lines, l => l.Date == "2029-01-03");
        Assert.Equal(1500m, wedLine.UnitPrice);
        Assert.Equal(12000m, wedLine.Amount);
        Assert.Contains("site rates", wedLine.Explanation);
        var satLine = Assert.Single(detail.Lines, l => l.Date == "2029-01-06");
        Assert.Equal(2000m, satLine.UnitPrice); // fell back to contract Saturday rate
        Assert.Equal(16000m, satLine.Amount);
    }

    /// <summary>A newer override version applies from its effective date;
    /// earlier bookings still bill at the previous override.</summary>
    [Fact]
    public async Task Billing_SiteOverride_Versioned()
    {
        var ids = await GraphAsync("site-ver", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        var site = await AddLocationAsync(ids.ProjectId, "Versioned Site");
        await Admin.PostAsJsonAsync($"/api/locations/{site}/rates",
            new { effectiveFrom = "2026-01-01", hourlyRate = 1200m });
        await Admin.PostAsJsonAsync($"/api/locations/{site}/rates",
            new { effectiveFrom = "2029-03-10", hourlyRate = 1800m });

        // Fri 2029-03-09 → old override; Mon 2029-03-12 → new override.
        foreach (var date in new[] { "2029-03-09", "2029-03-12" })
        {
            var bid = await ConfirmedBookingAsync(ids, date, site);
            var entry = await Consultant.PostAsJsonAsync("/api/time-entries", new
            {
                projectId = ids.ProjectId, date, category = "work",
                actualHours = 8m, bookingId = bid,
            });
            entry.EnsureSuccessStatusCode();
        }
        // Submit + approve ISO week 11 (Mar 9) and week 12 (Mar 12) separately.
        foreach (var monday in new[] { "2029-03-05", "2029-03-12" })
        {
            var dt = DateOnly.Parse(monday).ToDateTime(TimeOnly.MinValue);
            var submit = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
                new { year = System.Globalization.ISOWeek.GetYear(dt),
                      isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(dt) });
            submit.EnsureSuccessStatusCode();
            var sheetId = (await submit.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
                .GetProperty("sheet").GetProperty("id").GetGuid();
            (await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null))
                .EnsureSuccessStatusCode();
        }

        var detail = await InvoiceAsync(ids, 2029, 3);
        Assert.Equal(9600m, Assert.Single(detail.Lines, l => l.Date == "2029-03-09").Amount);
        Assert.Equal(14400m, Assert.Single(detail.Lines, l => l.Date == "2029-03-12").Amount);
    }

    /// <summary>On a day-rate contract a row flagged 'hours' bills plain
    /// hourly while the default 'contract' row prices as a day.</summary>
    [Fact]
    public async Task Billing_HoursMode_Bypasses_DayRate()
    {
        var ids = await GraphAsync("hours-mode", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "dailyRate", padEnabled = false,
            dayRate = 10000m, hourlyRate = 500m,
        });
        await ApproveEntriesAsync(ids, "2029-04-02",
            new { projectId = ids.ProjectId, date = "2029-04-02", category = "work",
                  actualHours = 8m },                                     // contract → day
            new { projectId = ids.ProjectId, date = "2029-04-03", category = "work",
                  actualHours = 8m, billing = "hours" });                 // explicit hours

        var detail = await InvoiceAsync(ids, 2029, 4);
        var day = Assert.Single(detail.Lines, l => l.Date == "2029-04-02");
        Assert.Equal(1m, day.Quantity);           // one day
        Assert.Equal(10000m, day.Amount);
        var hrs = Assert.Single(detail.Lines, l => l.Date == "2029-04-03");
        Assert.Equal(8m, hrs.Quantity);
        Assert.Equal(500m, hrs.UnitPrice);
        Assert.Equal(4000m, hrs.Amount);
        Assert.Contains("billed as hours", hrs.Explanation);
    }

    /// <summary>Mobilisation fee resolves from the site's override when the
    /// period is bound to a location; unbound periods use the contract fee.</summary>
    [Fact]
    public async Task Billing_Mobilisation_SiteFee()
    {
        var ids = await GraphAsync("mob-site", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false,
            hourlyRate = 1000m, mobilisationFee = 500m,
        });
        var site = await AddLocationAsync(ids.ProjectId, "Remote Site");
        await Admin.PostAsJsonAsync($"/api/locations/{site}/rates",
            new { effectiveFrom = "2026-01-01", mobilisationFee = 999m });

        var mob = await Admin.PostAsJsonAsync("/api/mobilisations", new
        {
            projectId = ids.ProjectId, resourceId = LinkedId,
            startDate = "2029-05-05", endDate = "2029-05-07", locationId = site,
        });
        mob.EnsureSuccessStatusCode();

        var detail = await InvoiceAsync(ids, 2029, 5);
        var line = Assert.Single(detail.Lines, l => l.Kind == "mobilisation");
        Assert.Equal(999m, line.Amount);
        Assert.Contains("Remote Site", line.Description);
    }

    // ── Project access + /mine ─────────────────────────────────────────────

    [Fact]
    public async Task Access_Granted_Resource_SelfReports_WithoutBooking()
    {
        var ids = await GraphAsync("acc-ok", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        // Lock the project down to a DIFFERENT resource first.
        var grant = await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/access",
            new { resourceId = ids.ResourceId });
        grant.EnsureSuccessStatusCode();

        var denied = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2029-06-04",
            category = "work", actualHours = 1m, description = "customer meeting",
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Grant the linked resource → the same write succeeds.
        (await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/access",
            new { resourceId = LinkedId })).EnsureSuccessStatusCode();
        var ok = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2029-06-04",
            category = "work", actualHours = 1m, description = "customer meeting",
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var listed = await Admin.GetFromJsonAsync<AccessDto[]>(
            $"/api/projects/{ids.ProjectId}/access");
        Assert.Equal(2, listed!.Length);
    }

    [Fact]
    public async Task Access_BookingHolder_CanWrite_WithoutListEntry()
    {
        var ids = await GraphAsync("acc-book", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        // Restrict to the scaffold resource; the linked resource only has a booking.
        (await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/access",
            new { resourceId = ids.ResourceId })).EnsureSuccessStatusCode();
        var bid = await ConfirmedBookingAsync(ids, "2029-06-11");

        var ok = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2029-06-11", category = "work",
            actualHours = 8m, bookingId = bid,
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Access_PmMayGrant_ResourceCannot()
    {
        var ids = await GraphAsync("acc-role", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        var asResource = await Consultant.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/access", new { resourceId = LinkedId });
        Assert.Equal(HttpStatusCode.Forbidden, asResource.StatusCode);
        var asPm = await PM.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/access", new { resourceId = LinkedId });
        Assert.Equal(HttpStatusCode.OK, asPm.StatusCode);
        var revoke = await PM.DeleteAsync(
            $"/api/projects/{ids.ProjectId}/access/{LinkedId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
    }

    [Fact]
    public async Task ProjectsMine_ResourceSees_AccessibleAndBooked_Only()
    {
        var idsA = await GraphAsync("mine-a", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        var idsB = await GraphAsync("mine-b", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        var idsC = await GraphAsync("mine-c", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = false, hourlyRate = 1000m,
        });
        // A: explicit access. B: a booking only. C: neither.
        (await Admin.PostAsJsonAsync($"/api/projects/{idsA.ProjectId}/access",
            new { resourceId = LinkedId })).EnsureSuccessStatusCode();
        await ConfirmedBookingAsync(idsB, "2029-07-02");

        var mine = await Consultant.GetFromJsonAsync<ProjectDto[]>("/api/projects/mine");
        var mineIds = mine!.Select(p => p.Id).ToHashSet();
        Assert.Contains(idsA.ProjectId, mineIds);
        Assert.Contains(idsB.ProjectId, mineIds);
        Assert.DoesNotContain(idsC.ProjectId, mineIds);

        // Admin gets the full active list (superset).
        var adminMine = await Admin.GetFromJsonAsync<ProjectDto[]>("/api/projects/mine");
        Assert.Contains(idsC.ProjectId, adminMine!.Select(p => p.Id));
    }
}
