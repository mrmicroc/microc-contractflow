using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>Personal .ics feed: token mint/rotate, anonymous fetch, authz.</summary>
public class CalendarTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);

    private async Task<Scaffold.Ids> GraphAsync(string tag) =>
        await Scaffold.CommercialGraphAsync(Admin, tag);

    private static string FeedTokenOf(string url) =>
        url.Split('/').Last().Replace(".ics", "");

    [Fact]
    public async Task FeedUrl_AsAdmin_CreatesTokenAndReturnsUrl()
    {
        var ids = await GraphAsync("cal-url");
        var res = await Admin.GetAsync($"/api/calendar/feed-url?resourceId={ids.ResourceId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("url").GetString()!;
        Assert.StartsWith($"/api/calendar/{ids.ResourceId}/", url);
        Assert.EndsWith(".ics", url);
        Assert.Equal(32, FeedTokenOf(url).Length); // Guid "N" format
    }

    [Fact]
    public async Task Feed_Anonymous_ReturnsVcalendarWithBooking()
    {
        var admin = Admin;
        var ids = await GraphAsync("cal-feed");
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-15");
        var url = (await (await admin.GetAsync(
            $"/api/calendar/feed-url?resourceId={ids.ResourceId}"))
            .Content.ReadFromJsonAsync<JsonElement>())!.GetProperty("url").GetString()!;

        var anon = factory.CreateClient(); // no dev-user header — feed is token-gated
        var res = await anon.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var ics = await res.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains(booking.Id.ToString(), ics);
        Assert.Contains("DTSTART;VALUE=DATE:20270615", ics);
    }

    [Fact]
    public async Task Feed_ExcludesCancelledBookings()
    {
        var admin = Admin;
        var ids = await GraphAsync("cal-cxl");
        var booking = await Scaffold.CreateBookingAsync(admin, ids, "2027-06-16");
        await admin.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        var url = (await (await admin.GetAsync(
            $"/api/calendar/feed-url?resourceId={ids.ResourceId}"))
            .Content.ReadFromJsonAsync<JsonElement>())!.GetProperty("url").GetString()!;

        var ics = await factory.CreateClient().GetStringAsync(url);
        Assert.DoesNotContain("BEGIN:VEVENT", ics);
    }

    [Fact]
    public async Task Rotate_InvalidatesOldUrl()
    {
        var admin = Admin;
        var ids = await GraphAsync("cal-rot");
        var url1 = (await (await admin.GetAsync(
            $"/api/calendar/feed-url?resourceId={ids.ResourceId}"))
            .Content.ReadFromJsonAsync<JsonElement>())!.GetProperty("url").GetString()!;
        var url2 = (await (await admin.PostAsync(
            $"/api/calendar/feed-url/rotate?resourceId={ids.ResourceId}", null))
            .Content.ReadFromJsonAsync<JsonElement>())!.GetProperty("url").GetString()!;

        Assert.NotEqual(url1, url2);
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(url1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(url2)).StatusCode);
    }

    [Fact]
    public async Task Feed_WrongToken_Returns404()
    {
        var ids = await GraphAsync("cal-404");
        var anon = factory.CreateClient();
        var res = await anon.GetAsync(
            $"/api/calendar/{ids.ResourceId}/{Guid.NewGuid():N}.ics");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task FeedUrl_AsResource_ResolvesOwnResource()
    {
        var ownResourceId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Resources.Add(new ContractFlow.Api.Models.Domain.Resource
            {
                Id = ownResourceId,
                DisplayName = "Self Tester",
                Type = ContractFlow.Api.Models.Domain.ResourceType.Employee,
                UserId = DevSeed.ResourceId,
            });
            return db.SaveChangesAsync();
        });

        var resourceClient = factory.CreateUserClient(DevSeed.ResourceId);
        var res = await resourceClient.GetAsync("/api/calendar/feed-url");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var url = (await res.Content.ReadFromJsonAsync<JsonElement>())!
            .GetProperty("url").GetString()!;
        Assert.StartsWith($"/api/calendar/{ownResourceId}/", url);
    }

    [Fact]
    public async Task FeedUrl_AsResource_ForOtherResource_IsForbidden()
    {
        var ids = await GraphAsync("cal-other");
        var resourceClient = factory.CreateUserClient(DevSeed.ResourceId);
        var res = await resourceClient.GetAsync(
            $"/api/calendar/feed-url?resourceId={ids.ResourceId}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
