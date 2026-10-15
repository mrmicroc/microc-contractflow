using System.Net.Http.Json;

namespace ContractFlow.Api.Tests.Infrastructure;

/// <summary>Creates a minimal commercial graph (customer→contract→project→resource)
/// for tests that need somewhere to hang bookings.</summary>
public static class Scaffold
{
    public record Ids(Guid CustomerId, Guid ContractId, Guid ProjectId, Guid ResourceId);
    public record IdRef(Guid Id);

    public static async Task<Ids> CommercialGraphAsync(HttpClient admin, string tag)
    {
        var customer = await (await admin.PostAsJsonAsync("/api/customers",
            new { name = $"Test Org {tag}" })).Content.ReadFromJsonAsync<IdRef>();
        var contract = await (await admin.PostAsJsonAsync("/api/contracts", new
        {
            customerId = customer!.Id, name = $"Contract {tag}",
            model = "padBased", startDate = "2026-01-01",
        })).Content.ReadFromJsonAsync<IdRef>();
        await admin.PostAsJsonAsync($"/api/contracts/{contract!.Id}/terms",
            new { effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE" });
        var project = await (await admin.PostAsJsonAsync("/api/projects", new
        {
            contractId = contract!.Id, name = $"Project {tag}",
        })).Content.ReadFromJsonAsync<IdRef>();
        var resource = await (await admin.PostAsJsonAsync("/api/resources", new
        {
            displayName = $"Tester {tag}", type = "subcontractor",
        })).Content.ReadFromJsonAsync<IdRef>();

        return new Ids(customer!.Id, contract!.Id, project!.Id, resource!.Id);
    }

    public static async Task<IdRef> CreateBookingAsync(
        HttpClient client, Ids ids, string date, string status = "requested", decimal hours = 8m)
    {
        var res = await client.PostAsJsonAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId, date,
            plannedHours = hours, status,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!;
    }

    public static Task<HttpResponseMessage> SetBaselineAsync(
        HttpClient admin, Ids ids, int year, int month, decimal amount) =>
        admin.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/pad/{year}/{month}/baseline",
            new { amount });
}
