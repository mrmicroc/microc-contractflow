using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Month-end close (design §6.4): carry-before-baseline arithmetic, the
/// pre-close checklist + preview, forced closes, ordering rules, closed-month
/// guards on booking actions, and the role matrix of the PAD endpoints.
/// </summary>
public class MonthCloseTests : IClassFixture<ContractFlowWebApplicationFactory>
{
    private readonly ContractFlowWebApplicationFactory factory;

    public MonthCloseTests(ContractFlowWebApplicationFactory factory)
    {
        this.factory = factory;
        factory.Clock.Set(new DateOnly(2031, 1, 15));
    }

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Manager => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private Task<HttpResponseMessage> CloseAsync(Scaffold.Ids ids, int y, int m, object? body = null) =>
        body is null
            ? Admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/{y}/{m}/close", null)
            : Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/pad/{y}/{m}/close", body);

    private Task<JsonElement> DashAsync(Scaffold.Ids ids, int y, int m) =>
        Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/{y}/{m}");

    private async Task UseDaysAsync(Scaffold.Ids ids, int y, int m, int count, int firstDay = 1)
    {
        for (var i = 0; i < count; i++)
        {
            var b = await Scaffold.CreateBookingAsync(Admin, ids,
                new DateOnly(y, m, firstDay + i).ToString("yyyy-MM-dd"));
            (await Admin.PostAsync($"/api/bookings/{b.Id}/confirm", null)).EnsureSuccessStatusCode();
            (await Admin.PostAsync($"/api/bookings/{b.Id}/complete", null)).EnsureSuccessStatusCode();
        }
    }

    private static string[] Codes(JsonElement preview, string prop) =>
        preview.GetProperty(prop).EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String
                ? e.GetString()! : e.GetProperty("code").GetString()!)
            .ToArray();

    [Fact]
    public async Task Close_CarryConsumedBeforeBaseline()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "carryfirst");
        // Jan: baseline 10, 3 used → 7 unused, 20 % = 1.4 → 2 carry into Feb.
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 1, 10m);
        await UseDaysAsync(ids, 2029, 1, 3);
        (await CloseAsync(ids, 2029, 1)).EnsureSuccessStatusCode();

        // Feb: baseline 10 + carry-in 2, 11 used → carry eaten first, baseline
        // 9 used → 1 unused; 20 % of 1 rounds up to 1 → 1 carries, 0 expires.
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 2, 10m);
        await UseDaysAsync(ids, 2029, 2, 11);
        var close = await CloseAsync(ids, 2029, 2);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var result = await close.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1m, result.GetProperty("carryOverOut").GetDecimal());
        Assert.Equal(0m, result.GetProperty("expired").GetDecimal());

        var mar = await DashAsync(ids, 2029, 3);
        Assert.Equal(1m, mar.GetProperty("carryOverIn").GetDecimal());
    }

    [Fact]
    public async Task Preview_ShowsChecklistAndSteps_WritesNothing()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "preview");
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 4, 10m);
        await UseDaysAsync(ids, 2029, 4, 7, firstDay: 2);
        var open = await Scaffold.CreateBookingAsync(Admin, ids, "2029-04-20");
        await Admin.PostAsync($"/api/bookings/{open.Id}/confirm", null); // left unresolved

        var before = (await DashAsync(ids, 2029, 4)).GetProperty("transactions").GetArrayLength();
        var preview = await Admin.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{ids.ProjectId}/pad/2029/4/close/preview");

        Assert.False(preview.GetProperty("canClose").GetBoolean());
        Assert.Contains("unresolvedBookings", Codes(preview, "checklist"));
        Assert.Contains("carryCap", Codes(preview, "steps"));
        // 3 unused → 20 % = 0.6 → 1 carried; the cap step explains its basis.
        Assert.Equal(1m, preview.GetProperty("result").GetProperty("carryOverOut").GetDecimal());
        var capStep = preview.GetProperty("steps").EnumerateArray()
            .First(s => s.GetProperty("code").GetString() == "carryCap");
        Assert.Contains("unused baseline 3", capStep.GetProperty("explanation").GetString());
        var after = (await DashAsync(ids, 2029, 4)).GetProperty("transactions").GetArrayLength();
        Assert.Equal(before, after);
        Assert.False((await DashAsync(ids, 2029, 4)).GetProperty("closed").GetBoolean());
    }

    [Fact]
    public async Task Close_UnresolvedBooking_Blocks_UntilForcedWithReason()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "blocked");
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 5, 3m);
        var b = await Scaffold.CreateBookingAsync(Admin, ids, "2029-05-08");
        await Admin.PostAsync($"/api/bookings/{b.Id}/confirm", null);

        var blocked = await CloseAsync(ids, 2029, 5);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("closeBlocked", body.GetProperty("error").GetString());
        Assert.Contains("unresolvedBookings", Codes(body.GetProperty("preview"), "checklist"));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await CloseAsync(ids, 2029, 5, new { force = true })).StatusCode);

        var forced = await CloseAsync(ids, 2029, 5,
            new { force = true, reason = "Customer confirmed the day was never worked" });
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.Equal(1m, (await forced.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("releasedOpenCommitments").GetDecimal());
    }

    [Fact]
    public async Task Close_UnsubmittedTime_And_PendingAdditionalPad_Block()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "blocked2");
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 6, 3m);
        (await Admin.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId,
            date = "2029-06-05", actualHours = 4m,
        })).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2029, month = 6, quantity = 1m,
        })).EnsureSuccessStatusCode();

        var res = await CloseAsync(ids, 2029, 6);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var codes = Codes((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preview"), "checklist");
        Assert.Contains("unsubmittedTime", codes);
        Assert.Contains("pendingAdditionalPad", codes);
    }

    [Fact]
    public async Task Close_Ordering_PreviousOpen_NextClosed_NotEnded()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "order");
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 8, 2m);
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 9, 2m);

        // September can't close while August (with ledger rows) is open.
        var early = await CloseAsync(ids, 2029, 9);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("previousMonthOpen",
            Codes((await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preview"), "orderingErrors"));

        (await CloseAsync(ids, 2029, 8)).EnsureSuccessStatusCode();
        (await CloseAsync(ids, 2029, 9)).EnsureSuccessStatusCode();

        // A month whose successor is closed can't close (carry would land in
        // a sealed month).
        var ids2 = await Scaffold.CommercialGraphAsync(Admin, "order2");
        await Scaffold.SetBaselineAsync(Admin, ids2, 2029, 11, 2m);
        (await CloseAsync(ids2, 2029, 11)).EnsureSuccessStatusCode();
        var reverse = await CloseAsync(ids2, 2029, 10);
        Assert.Equal(HttpStatusCode.Conflict, reverse.StatusCode);
        Assert.Contains("nextMonthClosed",
            Codes((await reverse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preview"), "orderingErrors"));

        // The current month hasn't ended (clock: 2031-01-15).
        var ids3 = await Scaffold.CommercialGraphAsync(Admin, "order3");
        var running = await CloseAsync(ids3, 2031, 1, new { force = true, reason = "try" });
        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
        Assert.Contains("monthNotEnded",
            Codes((await running.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preview"), "orderingErrors"));
    }

    [Fact]
    public async Task BookingActions_InClosedMonth_Return409()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "sealedacts");
        await Scaffold.SetBaselineAsync(Admin, ids, 2029, 12, 5m);
        var confirmed = await Scaffold.CreateBookingAsync(Admin, ids, "2029-12-04");
        await Admin.PostAsync($"/api/bookings/{confirmed.Id}/confirm", null);
        (await CloseAsync(ids, 2029, 12, new { force = true, reason = "test" })).EnsureSuccessStatusCode();

        foreach (var action in new[] { "start", "complete", "cancel" })
        {
            var res = await Admin.PostAsync($"/api/bookings/{confirmed.Id}/{action}", null);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        }
        var resched = await Admin.PostAsJsonAsync($"/api/bookings/{confirmed.Id}/reschedule",
            new { date = "2030-01-08" });
        Assert.Equal(HttpStatusCode.Conflict, resched.StatusCode);
    }

    [Fact]
    public async Task InvalidYear_IsBadRequest()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "badyear");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.GetAsync($"/api/projects/{ids.ProjectId}/pad/0/5")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Scaffold.SetBaselineAsync(Admin, ids, 99999, 5, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CloseAsync(ids, 1, 5)).StatusCode);
    }

    // ── Roles ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Manager_CannotSetBaselineOrClose_AdminCan()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "pmpad");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Scaffold.SetBaselineAsync(Manager, ids, 2029, 3, 4m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Manager.PostAsync(
            $"/api/projects/{ids.ProjectId}/pad/2029/3/close", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Scaffold.SetBaselineAsync(Admin, ids, 2029, 3, 4m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(ids, 2029, 3)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_And_Preview_ManagerFinanceRead_ResourceForbidden()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "padread");
        foreach (var c in new[] { Manager, Finance })
        {
            Assert.Equal(HttpStatusCode.OK,
                (await c.GetAsync($"/api/projects/{ids.ProjectId}/pad/2029/7")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await c.GetAsync($"/api/projects/{ids.ProjectId}/pad/2029/7/close/preview")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync($"/api/projects/{ids.ProjectId}/pad/2029/7/close/preview")).StatusCode);
    }
}
