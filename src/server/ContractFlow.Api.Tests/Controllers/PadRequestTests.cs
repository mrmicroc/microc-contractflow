using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Additional PAD requests (design §3, §4.3): linked prepared bookings,
/// partial availability, and the allowed + forbidden path of every decision.
/// </summary>
public class PadRequestTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record RequestDto(Guid Id, string Status, Guid? BookingId);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Manager => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private async Task<RequestDto> CreateAsync(Scaffold.Ids ids, int y, int m, decimal qty, Guid? bookingId = null)
    {
        var res = await Manager.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = y, month = m, quantity = qty, bookingId,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RequestDto>())!;
    }

    [Fact]
    public async Task Approve_ConfirmsLinkedPreparedBooking()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "linked");
        var b = await Scaffold.CreateBookingAsync(Admin, ids, "2028-03-07");
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.PostAsync($"/api/bookings/{b.Id}/confirm", null)).StatusCode); // no PAD

        var req = await CreateAsync(ids, 2028, 3, 1m, b.Id);
        Assert.Equal(b.Id, req.BookingId);
        (await Admin.PostAsync($"/api/pad-requests/{req.Id}/approve", null)).EnsureSuccessStatusCode();

        var booking = await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{b.Id}");
        Assert.Equal("confirmed", booking.GetProperty("status").GetString());
        var dash = await Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/2028/3");
        Assert.Equal(1m, dash.GetProperty("committedOpen").GetDecimal());
        Assert.Equal(0m, dash.GetProperty("free").GetDecimal());
    }

    [Fact]
    public async Task Create_WithBookingFromAnotherMonth_BadRequest()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "linked-bad");
        var b = await Scaffold.CreateBookingAsync(Admin, ids, "2028-04-04");
        var res = await Manager.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2028, month = 5, quantity = 1m, bookingId = b.Id,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PartialAvailability_HalfFreePlusHalfAdditional_Confirms()
    {
        // §4.3: a Booked Day may combine 0.50 free PAD + 0.50 approved Additional.
        var ids = await Scaffold.CommercialGraphAsync(Admin, "partial");
        await Scaffold.SetBaselineAsync(Admin, ids, 2028, 6, 0.5m);
        var b = await Scaffold.CreateBookingAsync(Admin, ids, "2028-06-06");
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.PostAsync($"/api/bookings/{b.Id}/confirm", null)).StatusCode);

        var req = await CreateAsync(ids, 2028, 6, 0.5m);
        (await Admin.PostAsync($"/api/pad-requests/{req.Id}/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/bookings/{b.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Reject_ManagerForbidden_AdminAllowed()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "reject");
        var req = await CreateAsync(ids, 2028, 7, 1m);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Manager.PostAsync($"/api/pad-requests/{req.Id}/reject", null)).StatusCode);
        var ok = await Admin.PostAsync($"/api/pad-requests/{req.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("rejected", (await ok.Content.ReadFromJsonAsync<RequestDto>())!.Status);
    }

    [Fact]
    public async Task Withdraw_FinanceAndResourceForbidden_ManagerAllowed()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "withdraw");
        var req = await CreateAsync(ids, 2028, 8, 1m);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Finance.PostAsync($"/api/pad-requests/{req.Id}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.PostAsync($"/api/pad-requests/{req.Id}/withdraw", null)).StatusCode);
        var ok = await Manager.PostAsync($"/api/pad-requests/{req.Id}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("withdrawn", (await ok.Content.ReadFromJsonAsync<RequestDto>())!.Status);
    }

    [Fact]
    public async Task Delete_FinanceAndResourceForbidden_ManagerAllowed()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "delreq");
        var req = await CreateAsync(ids, 2028, 9, 1m);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Finance.DeleteAsync($"/api/pad-requests/{req.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.DeleteAsync($"/api/pad-requests/{req.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Manager.DeleteAsync($"/api/pad-requests/{req.Id}")).StatusCode);
    }

    [Fact]
    public async Task Approve_Twice_SecondIsRejected()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "twice-req");
        var req = await CreateAsync(ids, 2028, 10, 1m);
        (await Admin.PostAsync($"/api/pad-requests/{req.Id}/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsync($"/api/pad-requests/{req.Id}/approve", null)).StatusCode);
        var dash = await Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/2028/10");
        Assert.Equal(1m, dash.GetProperty("additionalApproved").GetDecimal());
    }
}
