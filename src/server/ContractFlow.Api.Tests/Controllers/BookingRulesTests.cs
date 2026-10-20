using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Booking lifecycle rules (design §4.3, §6.1, §6.3, §7.1) and the booking
/// role matrix — every action's allowed and forbidden path. The clock is
/// pinned to Wed 2031-01-15 so notice periods are deterministic.
/// </summary>
public class BookingRulesTests : IClassFixture<ContractFlowWebApplicationFactory>
{
    private static readonly DateOnly Today = new(2031, 1, 15);
    private readonly ContractFlowWebApplicationFactory factory;

    public BookingRulesTests(ContractFlowWebApplicationFactory factory)
    {
        this.factory = factory;
        factory.Clock.Set(Today);
    }

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private Task<JsonElement> DashAsync(Scaffold.Ids ids, int y, int m) =>
        Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/{y}/{m}");

    private static decimal Dec(JsonElement e, string p) => e.GetProperty(p).GetDecimal();

    /// <summary>A graph whose resource is linked to the dev Resource user.</summary>
    private async Task<Scaffold.Ids> OwnGraphAsync(string tag)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        var linked = await (await Admin.PostAsJsonAsync("/api/resources", new
        {
            displayName = $"Linked {tag}", type = "employee", userId = DevSeed.ResourceId,
        })).Content.ReadFromJsonAsync<Scaffold.IdRef>();
        return ids with { ResourceId = linked!.Id };
    }

    private async Task<Scaffold.IdRef> ConfirmedAsync(Scaffold.Ids ids, string date, decimal hours = 8m)
    {
        var b = await Scaffold.CreateBookingAsync(Admin, ids, date, hours: hours);
        (await Admin.PostAsync($"/api/bookings/{b.Id}/confirm", null)).EnsureSuccessStatusCode();
        return b;
    }

    [Fact]
    public async Task List_EmptyRange_ReturnsEmptyArray()
    {
        var res = await Admin.GetAsync("/api/bookings?from=2040-01-01&to=2040-01-31");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task Reschedule_Forecast_StaysForecast_NoLedgerRows()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "rs-fc");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 3, 5m);
        var b = await Scaffold.CreateBookingAsync(Admin, ids, "2031-03-04", "forecast");

        var res = await Admin.PostAsJsonAsync($"/api/bookings/{b.Id}/reschedule", new { date = "2031-03-11" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("forecast", body.GetProperty("replacement").GetProperty("status").GetString());
        Assert.False(body.GetProperty("replacementConfirmed").GetBoolean());

        var dash = await DashAsync(ids, 2031, 3);
        Assert.Equal(1, dash.GetProperty("transactions").GetArrayLength()); // just the baseline
    }

    [Fact]
    public async Task Cancel_Late_ShortDay_Utilises1Pad()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "late-short");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 1, 5m);
        var b = await ConfirmedAsync(ids, "2031-01-16", hours: 4m); // tomorrow — late

        var res = await Admin.PostAsync($"/api/bookings/{b.Id}/cancel", null);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("utilised", body.GetProperty("padEffect").GetString());
        var dash = await DashAsync(ids, 2031, 1);
        Assert.Equal(1m, Dec(dash, "utilised")); // never below the 1.00 PAD minimum
        Assert.Equal(0m, Dec(dash, "committedOpen"));
    }

    [Fact]
    public async Task Cancel_RungWithHoursAndFee_ChargesBoth()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "hours-fee");
        (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 10m, padEnabled = true, padRate = 100m,
            cancellationSteps = new[] { new { minBusinessDaysBefore = 1, chargeHours = 4m, chargeAmount = 500m } },
        })).EnsureSuccessStatusCode();
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 1, 5m);
        var b = await ConfirmedAsync(ids, "2031-01-20"); // Mon — 3 business days ahead

        var body = await (await Admin.PostAsync($"/api/bookings/{b.Id}/cancel", null))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("partial", body.GetProperty("padEffect").GetString());
        Assert.Equal(500m, body.GetProperty("booking").GetProperty("cancellationChargeAmount").GetDecimal());
        Assert.Equal(0.5m, Dec(await DashAsync(ids, 2031, 1), "utilised"));
    }

    [Fact]
    public async Task Cancel_AfterTermsDropPad_StillReleasesCommitment()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "terms-flip");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 4, 5m);
        var b = await ConfirmedAsync(ids, "2031-04-15");
        // A later terms version without PAD takes effect before the booking.
        (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2031-04-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 10m,
        })).EnsureSuccessStatusCode();

        (await Admin.PostAsync($"/api/bookings/{b.Id}/cancel", null)).EnsureSuccessStatusCode();
        var dash = await DashAsync(ids, 2031, 4);
        Assert.Equal(0m, Dec(dash, "committedOpen"));
        Assert.Equal(5m, Dec(dash, "free"));
    }

    [Fact]
    public async Task Complete_WithActualHoursBeyondCommitment_AllocatesOverrun()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "overrun");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 1, 1.25m);
        var b = await ConfirmedAsync(ids, "2031-01-14");

        // 12 h actual = 1.5 PAD; 1.0 committed → 0.5 overrun: 0.25 free, 0.25 next month.
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 2, 3m);
        var res = await Admin.PostAsJsonAsync($"/api/bookings/{b.Id}/complete", new { actualHours = 12m });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var split = body.GetProperty("overrun");
        Assert.Equal(0.25m, Dec(split, "fromCurrentFree"));
        Assert.Equal(0.25m, Dec(split, "fromNextMonthBaseline"));
        Assert.Equal(0m, Dec(split, "autoAdditional"));
        Assert.Equal(1.5m, Dec(body, "padUtilised"));

        Assert.Equal(1.25m, Dec(await DashAsync(ids, 2031, 1), "utilised"));
        var feb = await DashAsync(ids, 2031, 2);
        Assert.Equal(0.25m, Dec(feb, "borrowedFromThisMonth"));
        Assert.Equal(2.75m, Dec(feb, "free"));
    }

    [Fact]
    public async Task Complete_FutureBooking_BadRequest()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "future");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 2, 2m);
        var b = await ConfirmedAsync(ids, "2031-02-03");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsync($"/api/bookings/{b.Id}/complete", null)).StatusCode);
    }

    [Fact]
    public async Task Acknowledge_LeavesLedgerUnchanged()
    {
        var ids = await OwnGraphAsync("ack-ledger");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 2, 2m);
        var b = await ConfirmedAsync(ids, "2031-02-10");
        var before = await DashAsync(ids, 2031, 2);

        (await Consultant.PostAsync($"/api/bookings/{b.Id}/acknowledge", null)).EnsureSuccessStatusCode();
        var after = await DashAsync(ids, 2031, 2);
        Assert.Equal(before.GetProperty("transactions").GetArrayLength(),
            after.GetProperty("transactions").GetArrayLength());
        Assert.Equal(1m, Dec(after, "committedOpen"));
    }

    [Fact]
    public async Task Update_WithoutPlannedHours_KeepsHours()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "keep-hours");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 2, 2m);
        var b = await ConfirmedAsync(ids, "2031-02-12", hours: 6m);
        var res = await Admin.PutAsJsonAsync($"/api/bookings/{b.Id}", new { workDescription = "Re-test" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(6m, Dec(await res.Content.ReadFromJsonAsync<JsonElement>(), "plannedHours"));
    }

    [Fact]
    public async Task Resource_SeesNoCustomerPricing()
    {
        var ids = await OwnGraphAsync("no-pricing");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 2, 2m);
        var b = await ConfirmedAsync(ids, "2031-02-17");

        var own = await Consultant.GetFromJsonAsync<JsonElement>($"/api/bookings/{b.Id}");
        Assert.Equal(JsonValueKind.Null, own.GetProperty("expectedPad").ValueKind);
        Assert.Equal(JsonValueKind.Null, own.GetProperty("cancellationChargeAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, own.GetProperty("cancellationChargeHours").ValueKind);

        var asAdmin = await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{b.Id}");
        Assert.Equal(1m, Dec(asAdmin, "expectedPad"));
    }

    // ── Role matrix ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Resource_CannotConfirmCancelRescheduleDeleteOrComplete()
    {
        var ids = await OwnGraphAsync("res-forbid");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 1, 5m);
        var planned = await Scaffold.CreateBookingAsync(Admin, ids, "2031-01-27");
        var active = await ConfirmedAsync(ids, "2031-01-13");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.PostAsync($"/api/bookings/{planned.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.PostAsync($"/api/bookings/{active.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.PostAsJsonAsync(
            $"/api/bookings/{active.Id}/reschedule", new { date = "2031-01-28" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.DeleteAsync($"/api/bookings/{planned.Id}")).StatusCode);
        // Utilisation follows MICROC approval — the resource can't complete.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.PostAsync($"/api/bookings/{active.Id}/complete", null)).StatusCode);

        // Allowed paths for the same actions.
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/bookings/{active.Id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.DeleteAsync($"/api/bookings/{planned.Id}")).StatusCode);
    }

    [Fact]
    public async Task Resource_CannotStartOrReadAnothersBooking_CanOwn()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "other-res");
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 2, 5m);
        var others = await ConfirmedAsync(ids, "2031-02-04"); // scaffold resource — not linked
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.PostAsync($"/api/bookings/{others.Id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync($"/api/bookings/{others.Id}")).StatusCode);

        var own = await ConfirmedAsync(ids with { ResourceId = (await OwnGraphAsync("other-res2")).ResourceId },
            "2031-02-05");
        Assert.Equal(HttpStatusCode.OK, (await Consultant.GetAsync($"/api/bookings/{own.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Consultant.PostAsync($"/api/bookings/{own.Id}/start", null)).StatusCode);
    }
}
