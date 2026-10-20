using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class PadTests : IClassFixture<ContractFlowWebApplicationFactory>
{
    private readonly ContractFlowWebApplicationFactory factory;

    public PadTests(ContractFlowWebApplicationFactory factory)
    {
        this.factory = factory;
        // Closing and completing need the months to lie in the past.
        factory.Clock.Set(new DateOnly(2031, 1, 15));
    }

    public record TxDto(string Type, decimal Amount, Guid? BookingId, string? Note);
    public record DashboardDto(decimal Baseline, decimal CarryOverIn,
        decimal AdditionalApproved, decimal CommittedOpen, decimal Utilised,
        decimal Expired, decimal CarryOverOut, decimal Free,
        bool Closed, TxDto[] Transactions);
    public record PadRequestDto(Guid Id, string Status, decimal Quantity);

    private Task<DashboardDto?> DashAsync(HttpClient c, Scaffold.Ids ids, int y, int m) =>
        c.GetFromJsonAsync<DashboardDto>($"/api/projects/{ids.ProjectId}/pad/{y}/{m}");

    [Fact]
    public async Task Baseline_SetIsDelta_AmendableWithAudit()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "delta");

        await Scaffold.SetBaselineAsync(admin, ids, 2027, 6, 10m);
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 6, 8m); // amend down

        var dash = await DashAsync(admin, ids, 2027, 6);
        Assert.Equal(8m, dash!.Baseline);
        // Both entries remain in the append-only trail.
        Assert.Equal(2, dash.Transactions.Count(t => t.Type == "baselineSet"));
    }

    [Fact]
    public async Task CloseMonth_SpecExample_Carries2Expires1()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "close");

        // Spec §5 worked example: baseline 10, used 7 → carry-over 2, expire 1.
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 7, 10m);
        for (var day = 1; day <= 7; day++)
        {
            var b = await Scaffold.CreateBookingAsync(admin, ids, $"2027-07-0{day + 1}");
            await admin.PostAsync($"/api/bookings/{b.Id}/confirm", null);
            await admin.PostAsync($"/api/bookings/{b.Id}/complete", null);
        }

        var close = await admin.PostAsync(
            $"/api/projects/{ids.ProjectId}/pad/2027/7/close", null);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var result = await close.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2m, result.GetProperty("carryOverOut").GetDecimal());
        Assert.Equal(1m, result.GetProperty("expired").GetDecimal());

        var dash = await DashAsync(admin, ids, 2027, 7);
        Assert.True(dash!.Closed);
        Assert.Equal(2m, dash.CarryOverOut);
        Assert.Equal(1m, dash.Expired);

        // Carry-over enters a month that has zero baseline (§4.3).
        var next = await DashAsync(admin, ids, 2027, 8);
        Assert.Equal(0m, next!.Baseline);
        Assert.Equal(2m, next.CarryOverIn);
        Assert.Equal(2m, next.Free);
    }

    [Fact]
    public async Task CloseMonth_CarryOver_ExpiresAfterConfiguredWindow()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "carrywin");
        // Two-month carry window — carried PAD survives one re-carry, then dies.
        await admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = true, padRate = 100m,
            carryOverMaxPercent = 50m, carryOverMonths = 2,
        });

        // Jan 2028: baseline 10, use 8 → 2 carries into Feb (cap allows 5).
        await Scaffold.SetBaselineAsync(admin, ids, 2028, 1, 10m);
        foreach (var day in new[] { "03", "04", "05", "06", "07", "10", "11", "12" })
        {
            var b = await Scaffold.CreateBookingAsync(admin, ids, $"2028-01-{day}");
            await admin.PostAsync($"/api/bookings/{b.Id}/confirm", null);
            await admin.PostAsync($"/api/bookings/{b.Id}/complete", null);
        }
        await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2028/1/close", null);
        Assert.Equal(2m, (await DashAsync(admin, ids, 2028, 2))!.CarryOverIn);

        // Feb: nothing used → the whole carry re-carries (origin age 1 < 2).
        await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2028/2/close", null);
        var feb = await DashAsync(admin, ids, 2028, 2);
        Assert.Equal(2m, feb!.CarryOverOut);
        Assert.Equal(0m, feb.Expired);
        Assert.Equal(2m, (await DashAsync(admin, ids, 2028, 3))!.CarryOverIn);

        // Mar: origin age hits the 2-month window → the carry finally expires.
        await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2028/3/close", null);
        var mar = await DashAsync(admin, ids, 2028, 3);
        Assert.Equal(2m, mar!.Expired);
        Assert.Equal(0m, mar.CarryOverOut);
        Assert.Equal(0m, (await DashAsync(admin, ids, 2028, 4))!.CarryOverIn);
    }

    [Fact]
    public async Task CloseMonth_Twice_ReturnsConflict()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "twice");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 9, 5m);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2027/9/close", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2027/9/close", null)).StatusCode);
    }

    [Fact]
    public async Task Baseline_OnClosedMonth_IsRejected()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "sealed");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 10, 5m);
        await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2027/10/close", null);

        var res = await Scaffold.SetBaselineAsync(admin, ids, 2027, 10, 9m);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Confirm_OnClosedMonth_IsRejected()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "sealed2");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 11, 5m);
        await admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2027/11/close", null);

        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-11-10");
        var res = await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("monthClosed",
            (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task AdditionalPad_Approval_UnblocksConfirmation()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "addpad");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 12, 1m);

        // Baseline covers 1 PAD; a second booking needs additional PAD.
        var b1 = await Scaffold.CreateBookingAsync(admin, ids, "2027-12-07");
        await admin.PostAsync($"/api/bookings/{b1.Id}/confirm", null);
        var b2 = await Scaffold.CreateBookingAsync(admin, ids, "2027-12-08");
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/bookings/{b2.Id}/confirm", null)).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2027, month = 12, quantity = 2m,
            reason = "Customer requested extra inspection days",
        });
        var request = await created.Content.ReadFromJsonAsync<PadRequestDto>();
        Assert.Equal("submitted", request!.Status);

        var approved = await admin.PostAsync($"/api/pad-requests/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var dash = await DashAsync(admin, ids, 2027, 12);
        Assert.Equal(2m, dash!.AdditionalApproved);

        var confirm = await admin.PostAsync($"/api/bookings/{b2.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    [Fact]
    public async Task PadRequest_Roles_ManagerCreates_AdminApproves_FinanceReads()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "roles");

        var manager = factory.CreateUserClient(DevSeed.ManagerId);
        var created = await manager.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2028, month = 1, quantity = 1m,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var request = await created.Content.ReadFromJsonAsync<PadRequestDto>();

        // Manager cannot approve their own request; admin can.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsync($"/api/pad-requests/{request!.Id}/approve", null)).StatusCode);

        var finance = factory.CreateUserClient(DevSeed.FinanceId);
        Assert.Equal(HttpStatusCode.OK,
            (await finance.GetAsync("/api/pad-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await finance.PostAsJsonAsync("/api/pad-requests", new
            {
                projectId = ids.ProjectId, year = 2028, month = 1, quantity = 1m,
            })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/pad-requests/{request.Id}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_AsResource_IsForbidden()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "padauth");
        var consultant = factory.CreateUserClient(DevSeed.ResourceId);
        var res = await consultant.GetAsync($"/api/projects/{ids.ProjectId}/pad/2027/6");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
