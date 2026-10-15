using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class BookingsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record BookingDto(Guid Id, string Status, string Date, decimal? ExpectedPad,
        decimal PlannedHours, string ResourceName, string[] Warnings,
        Guid? ReplacesBookingId, Guid? RescheduledToBookingId);
    public record ActionResultDto(BookingDto Booking, string? PadEffect, decimal? PadAmount);
    public record DashboardDto(decimal Baseline, decimal CommittedOpen, decimal Utilised,
        decimal Free, bool Closed);

    private async Task<Scaffold.Ids> NewGraphAsync(string tag)
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        return await Scaffold.CommercialGraphAsync(admin, tag);
    }

    private Task<DashboardDto?> DashboardAsync(HttpClient client, Scaffold.Ids ids, int y, int m) =>
        client.GetFromJsonAsync<DashboardDto>($"/api/projects/{ids.ProjectId}/pad/{y}/{m}");

    [Fact]
    public async Task Create_Forecast_DoesNotTouchLedger()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("fc");
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-03-09", "forecast");

        var dash = await DashboardAsync(admin, ids, 2027, 3);
        Assert.Equal(0m, dash!.CommittedOpen);
        var raw = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{ids.ProjectId}/pad/2027/3");
        Assert.Equal(0, raw.GetProperty("transactions").GetArrayLength());
    }

    [Fact]
    public async Task Confirm_WithoutBaseline_ReturnsInsufficientPad()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("nobase");
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-03-09");

        var res = await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficientPad", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Confirm_NoPadComponent_SkipsLedgerEntirely()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("nopad");
        // Flip the contract to plain hourly — this terms version (same
        // effectiveFrom, higher version) wins over the scaffold's PAD version.
        await admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms",
            new { effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
                  timeBilling = "hourly", hourlyRate = 10m });

        // No baseline at all — a non-PAD contract must not need one.
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-08");
        var res = await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<BookingDto>();
        Assert.Equal("confirmed", dto!.Status);
        Assert.Null(dto.ExpectedPad);

        var dash = await DashboardAsync(admin, ids, 2027, 6);
        Assert.Equal(0m, dash!.CommittedOpen);
    }

    [Fact]
    public async Task Confirm_ThenComplete_CommitsThenUtilises()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("flow");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 4, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-04-07");

        var confirmed = await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("confirmed",
            (await confirmed.Content.ReadFromJsonAsync<BookingDto>())!.Status);

        var dash = await DashboardAsync(admin, ids, 2027, 4);
        Assert.Equal(1m, dash!.CommittedOpen);
        Assert.Equal(4m, dash.Free);

        var completed = await admin.PostAsync($"/api/bookings/{booking.Id}/complete", null);
        var result = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", result.GetProperty("booking").GetProperty("status").GetString());
        Assert.Equal(1m, result.GetProperty("padUtilised").GetDecimal());

        dash = await DashboardAsync(admin, ids, 2027, 4);
        Assert.Equal(0m, dash!.CommittedOpen);
        Assert.Equal(1m, dash.Utilised);
        Assert.Equal(4m, dash.Free);
    }

    [Fact]
    public async Task Confirm_LongerDay_CommitsProportionally()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("long");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 5, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-05-12", hours: 12m);

        var res = await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dash = await DashboardAsync(admin, ids, 2027, 5);
        Assert.Equal(1.5m, dash!.CommittedOpen); // 12h / 8h-per-PAD
    }

    [Fact]
    public async Task Cancel_Timely_ReleasesPad()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("timely");
        var far = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await Scaffold.SetBaselineAsync(admin, ids, far.Year, far.Month, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, far.ToString("yyyy-MM-dd"));
        await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);

        var res = await admin.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("released", body.GetProperty("padEffect").GetString());
        Assert.Equal("cancelled",
            body.GetProperty("booking").GetProperty("status").GetString());

        var dash = await DashboardAsync(admin, ids, far.Year, far.Month);
        Assert.Equal(0m, dash!.CommittedOpen);
        Assert.Equal(5m, dash.Free);
    }

    [Fact]
    public async Task Cancel_Late_UtilisesPad()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("late");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        await Scaffold.SetBaselineAsync(admin, ids, tomorrow.Year, tomorrow.Month, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, tomorrow.ToString("yyyy-MM-dd"));
        await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);

        var res = await admin.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("utilised", body.GetProperty("padEffect").GetString());

        var dash = await DashboardAsync(admin, ids, tomorrow.Year, tomorrow.Month);
        Assert.Equal(1m, dash!.Utilised);
        Assert.Equal(4m, dash.Free);
    }

    [Fact]
    public async Task Reschedule_CancelsOriginal_AndConfirmsReplacement()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("resc");
        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var newDate = oldDate.AddDays(7);
        await Scaffold.SetBaselineAsync(admin, ids, oldDate.Year, oldDate.Month, 5m);
        if (newDate.Month != oldDate.Month)
            await Scaffold.SetBaselineAsync(admin, ids, newDate.Year, newDate.Month, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, oldDate.ToString("yyyy-MM-dd"));
        await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);

        var res = await admin.PostAsJsonAsync($"/api/bookings/{booking.Id}/reschedule",
            new { date = newDate.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();

        var original = body.GetProperty("original");
        var replacement = body.GetProperty("replacement");
        Assert.Equal("rescheduled", original.GetProperty("status").GetString());
        Assert.Equal("confirmed", replacement.GetProperty("status").GetString());
        Assert.Equal(booking.Id, replacement.GetProperty("replacesBookingId").GetGuid());
        Assert.True(body.GetProperty("replacementConfirmed").GetBoolean());
    }

    [Fact]
    public async Task Update_CommittedBooking_RejectsDateChange()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("locked");
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await Scaffold.SetBaselineAsync(admin, ids, date.Year, date.Month, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, date.ToString("yyyy-MM-dd"));
        await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);

        var res = await admin.PutAsJsonAsync($"/api/bookings/{booking.Id}",
            new { plannedHours = 8m, date = date.AddDays(1).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_ByOwningResource_Works_ByOther_IsForbidden()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("ack");
        // Link the dev consultant user to a resource and book that resource.
        var linked = await (await admin.PostAsJsonAsync("/api/resources", new
        {
            displayName = "Linked Tester", type = "employee", userId = DevSeed.ResourceId,
        })).Content.ReadFromJsonAsync<Scaffold.IdRef>();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await Scaffold.SetBaselineAsync(admin, ids, date.Year, date.Month, 5m);
        var ownIds = ids with { ResourceId = linked!.Id };
        var booking = await Scaffold.CreateBookingAsync(admin, ownIds, date.ToString("yyyy-MM-dd"));
        await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);

        // The resource acknowledges their own booking.
        var consultant = factory.CreateUserClient(DevSeed.ResourceId);
        var ok = await consultant.PostAsync($"/api/bookings/{booking.Id}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("acknowledged",
            (await ok.Content.ReadFromJsonAsync<BookingDto>())!.Status);

        // Finance can neither see nor act on bookings.
        var finance = factory.CreateUserClient(DevSeed.FinanceId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await finance.PostAsync($"/api/bookings/{booking.Id}/acknowledge", null)).StatusCode);
    }

    [Fact]
    public async Task List_ResourceSeesOnlyOwn()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("own");
        var linked = await (await admin.PostAsJsonAsync("/api/resources", new
        {
            displayName = "Mine Tester", type = "employee", userId = DevSeed.ResourceId,
        })).Content.ReadFromJsonAsync<Scaffold.IdRef>();

        await Scaffold.CreateBookingAsync(admin, ids, "2027-08-10"); // other resource
        var mine = await Scaffold.CreateBookingAsync(admin,
            ids with { ResourceId = linked!.Id }, "2027-08-10");

        var consultant = factory.CreateUserClient(DevSeed.ResourceId);
        var list = await consultant.GetFromJsonAsync<BookingDto[]>(
            "/api/bookings?from=2027-08-01&to=2027-08-31");
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(mine.Id, list[0].Id);

        var all = await admin.GetFromJsonAsync<BookingDto[]>(
            "/api/bookings?from=2027-08-01&to=2027-08-31");
        Assert.True(all!.Length >= 2);
    }

    [Fact]
    public async Task DoubleBooking_SameDay_ProducesWarning()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var ids = await NewGraphAsync("conflict");
        var b1 = await Scaffold.CreateBookingAsync(admin, ids, "2027-09-14");
        await Scaffold.CreateBookingAsync(admin, ids, "2027-09-14");

        var detail = await admin.GetFromJsonAsync<BookingDto>($"/api/bookings/{b1.Id}");
        Assert.Contains("doubleBooked", detail!.Warnings);
    }

    [Fact]
    public async Task Create_AsFinance_IsForbidden()
    {
        var finance = factory.CreateUserClient(DevSeed.FinanceId);
        var res = await finance.PostAsJsonAsync("/api/bookings", new
        {
            projectId = Guid.NewGuid(), resourceId = Guid.NewGuid(), date = "2027-01-05",
        });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
