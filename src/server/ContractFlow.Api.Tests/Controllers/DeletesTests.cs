using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Covers the admin delete surface: hard delete of creatable entities, the
/// dependency guards that protect audit anchors (bookings/ledger), and the
/// forbidden paths for non-admin roles.
/// </summary>
public class DeletesTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);

    private async Task<Scaffold.Ids> GraphAsync(string tag) =>
        await Scaffold.CommercialGraphAsync(Admin, tag);

    // ── customers ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Customer_WithoutContracts_Deletes204()
    {
        var res = await Admin.PostAsJsonAsync("/api/customers", new { name = "Solo Org" });
        var id = (await res.Content.ReadFromJsonAsync<Scaffold.IdRef>())!.Id;

        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.DeleteAsync($"/api/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Admin.GetAsync($"/api/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Customer_WithContract_Returns409()
    {
        var ids = await GraphAsync("cust-dep");
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/customers/{ids.CustomerId}")).StatusCode);
    }

    [Fact]
    public async Task Customer_Delete_AsManager_IsForbidden()
    {
        var ids = await GraphAsync("cust-authz");
        var pm = factory.CreateUserClient(DevSeed.ManagerId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await pm.DeleteAsync($"/api/customers/{ids.CustomerId}")).StatusCode);
    }

    // ── contracts & terms ───────────────────────────────────────────────────

    [Fact]
    public async Task Contract_WithBooking_Returns409()
    {
        var ids = await GraphAsync("ct-dep");
        await Scaffold.CreateBookingAsync(Admin, ids, "2027-06-01");
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/contracts/{ids.ContractId}")).StatusCode);
    }

    [Fact]
    public async Task Contract_Clean_DeletesAndCascades()
    {
        var ids = await GraphAsync("ct-clean");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.DeleteAsync($"/api/contracts/{ids.ContractId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Admin.GetAsync($"/api/contracts/{ids.ContractId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Admin.GetAsync($"/api/projects/{ids.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task Contract_WithAgreement_Returns409()
    {
        var admin = Admin;
        var ids = await GraphAsync("ct-agrm");
        await admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            contractId = ids.ContractId, effectiveFrom = "2026-01-01", currency = "SEK",
        });
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.DeleteAsync($"/api/contracts/{ids.ContractId}")).StatusCode);
    }

    [Fact]
    public async Task Terms_Version_Deletes()
    {
        var admin = Admin;
        var ids = await GraphAsync("terms-del");
        var terms = await admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{ids.ContractId}/terms");
        var termId = terms.EnumerateArray().First().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/contracts/{ids.ContractId}/terms/{termId}")).StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{ids.ContractId}/terms");
        Assert.Equal(0, after.GetArrayLength());
    }

    // ── projects ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_WithBooking_Returns409()
    {
        var ids = await GraphAsync("prj-dep");
        await Scaffold.CreateBookingAsync(Admin, ids, "2027-06-01");
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/projects/{ids.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task Project_Clean_Deletes()
    {
        var ids = await GraphAsync("prj-clean");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.DeleteAsync($"/api/projects/{ids.ProjectId}")).StatusCode);
    }

    // ── resources & agreements ──────────────────────────────────────────────

    [Fact]
    public async Task Resource_WithBooking_Returns409()
    {
        var ids = await GraphAsync("res-dep");
        await Scaffold.CreateBookingAsync(Admin, ids, "2027-06-01");
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/resources/{ids.ResourceId}")).StatusCode);
    }

    [Fact]
    public async Task Resource_Clean_DeletesWithAgreements()
    {
        var admin = Admin;
        var res = await admin.PostAsJsonAsync("/api/resources",
            new { displayName = "Temp Tester", type = "employee" });
        var id = (await res.Content.ReadFromJsonAsync<Scaffold.IdRef>())!.Id;
        await admin.PostAsJsonAsync($"/api/resources/{id}/agreements",
            new { effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 100 });

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/resources/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/resources/{id}")).StatusCode);
    }

    [Fact]
    public async Task Agreement_Version_Deletes()
    {
        var admin = Admin;
        var ids = await GraphAsync("agr-del");
        var created = await admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements",
            new { effectiveFrom = "2026-01-01", currency = "SEK" });
        var agreementId = (await created.Content.ReadFromJsonAsync<Scaffold.IdRef>())!.Id;

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/resources/{ids.ResourceId}/agreements/{agreementId}")).StatusCode);
    }

    // ── bookings ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Booking_PreCommitment_Deletes()
    {
        var admin = Admin;
        var ids = await GraphAsync("bk-del");
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-01", "tentative");

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/bookings/{booking.Id}")).StatusCode);
    }

    [Fact]
    public async Task Booking_Confirmed_Returns409_MustCancel()
    {
        var admin = Admin;
        var ids = await GraphAsync("bk-con");
        await Scaffold.SetBaselineAsync(admin, ids, 2027, 6, 5m);
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-01");
        await admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.DeleteAsync($"/api/bookings/{booking.Id}")).StatusCode);
    }

    [Fact]
    public async Task Booking_Delete_AsManager_Allowed_AsResource_Forbidden()
    {
        var admin = Admin;
        var ids = await GraphAsync("bk-authz");
        var b1 = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-01", "forecast");
        var b2 = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-02", "forecast");

        var pm = factory.CreateUserClient(DevSeed.ManagerId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await pm.DeleteAsync($"/api/bookings/{b1.Id}")).StatusCode);

        var resource = factory.CreateUserClient(DevSeed.ResourceId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await resource.DeleteAsync($"/api/bookings/{b2.Id}")).StatusCode);
    }

    // ── additional PAD requests ─────────────────────────────────────────────

    [Fact]
    public async Task PadRequest_Submitted_Deletes_Approved_Returns409()
    {
        var admin = Admin;
        var ids = await GraphAsync("req-del");
        var r1 = await (await admin.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2027, month = 6, quantity = 2,
        })).Content.ReadFromJsonAsync<Scaffold.IdRef>();
        var r2 = await (await admin.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2027, month = 6, quantity = 3,
        })).Content.ReadFromJsonAsync<Scaffold.IdRef>();

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/pad-requests/{r1!.Id}")).StatusCode);

        await admin.PostAsync($"/api/pad-requests/{r2!.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.DeleteAsync($"/api/pad-requests/{r2.Id}")).StatusCode);
    }
}
