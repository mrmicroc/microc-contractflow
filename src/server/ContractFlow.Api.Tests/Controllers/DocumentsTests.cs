using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Document attachments (design §3): expense receipts + booking evidence —
/// upload/list/download/delete, ownership scoping, validation and cleanup
/// when the owner is deleted.
/// </summary>
public class DocumentsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record DocDto(Guid Id, string FileName, string ContentType, long SizeBytes);
    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);
    private HttpClient Reviewer => factory.CreateUserClient(DevSeed.ReviewerId);

    private async Task<Guid> LinkedResourceAsync()
    {
        var linked = TimeReportingTests.LinkedId;
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == linked))
            {
                db.Resources.Add(new Resource
                {
                    Id = linked,
                    DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor,
                    UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        return linked;
    }

    private async Task<IdRef> PostExpenseAsync(HttpClient client, string date)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, $"doc{Guid.NewGuid():N}"[..14]);
        var res = await client.PostAsJsonAsync("/api/expenses", new
        {
            projectId = ids.ProjectId, date, category = "meal", amount = 120m,
            currency = "SEK", reimbursableToResource = true,
            rechargeableToCustomer = false, includedInRate = false,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!;
    }

    private static MultipartFormDataContent Upload(string name, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", name);
        return content;
    }

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public async Task Resource_Uploads_Lists_Downloads_Deletes_Receipt()
    {
        await LinkedResourceAsync();
        var expense = await PostExpenseAsync(Consultant, "2027-10-04");
        var bytes = Png();

        var up = await Consultant.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={expense.Id}",
            Upload("kvitto.png", "image/png", bytes));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var doc = await up.Content.ReadFromJsonAsync<DocDto>();
        Assert.Equal("kvitto.png", doc!.FileName);

        var list = await Consultant.GetFromJsonAsync<DocDto[]>(
            $"/api/documents?ownerType=expense&ownerId={expense.Id}");
        Assert.Single(list!);

        var got = await Consultant.GetAsync($"/api/documents/{doc.Id}/content");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode);
        Assert.Equal(bytes, await got.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent,
            (await Consultant.DeleteAsync($"/api/documents/{doc.Id}")).StatusCode);
        Assert.Empty(await Consultant.GetFromJsonAsync<DocDto[]>(
            $"/api/documents?ownerType=expense&ownerId={expense.Id}") ?? []);
    }

    [Fact]
    public async Task Resource_Cannot_Attach_To_Other_Resources_Expense()
    {
        await LinkedResourceAsync();
        // Admin creates an expense owned by a *different* (unlinked) resource.
        var ids = await Scaffold.CommercialGraphAsync(Admin, "otherdoc");
        var res = await Admin.PostAsJsonAsync("/api/expenses", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId, date = "2027-10-05",
            category = "other", amount = 50m, currency = "SEK",
            reimbursableToResource = true, rechargeableToCustomer = false,
            includedInRate = false,
        });
        var expense = await res.Content.ReadFromJsonAsync<IdRef>();

        var up = await Consultant.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={expense!.Id}",
            Upload("kvitto.png", "image/png", Png()));
        Assert.Equal(HttpStatusCode.Forbidden, up.StatusCode);
    }

    [Fact]
    public async Task Admin_Uploads_Booking_Evidence()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "bookev");
        var booking = await Scaffold.CreateBookingAsync(Admin, ids, "2027-10-12");

        var up = await Admin.PostAsync(
            $"/api/documents?ownerType=booking&ownerId={booking.Id}",
            Upload("beslut.pdf", "application/pdf", "%PDF-1.4 fake"u8.ToArray()));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
    }

    [Fact]
    public async Task CustomerReviewer_Has_No_Access()
    {
        await LinkedResourceAsync();
        var expense = await PostExpenseAsync(Consultant, "2027-10-06");
        Assert.Equal(HttpStatusCode.Forbidden, (await Reviewer.GetAsync(
            $"/api/documents?ownerType=expense&ownerId={expense.Id}")).StatusCode);
    }

    [Fact]
    public async Task Validation_Rejects_Bad_Type_And_Missing_Owner()
    {
        await LinkedResourceAsync();
        var expense = await PostExpenseAsync(Consultant, "2027-10-07");
        var bad = await Admin.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={expense.Id}",
            Upload("skadlig.exe", "application/x-msdownload", [0x4D, 0x5A]));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ghost = await Admin.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={Guid.NewGuid()}",
            Upload("kvitto.png", "image/png", Png()));
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
    }

    [Fact]
    public async Task Deleting_Expense_Removes_Its_Documents()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "cascade");
        var res = await Admin.PostAsJsonAsync("/api/expenses", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId, date = "2027-10-08",
            category = "material", amount = 200m, currency = "SEK",
            reimbursableToResource = false, rechargeableToCustomer = true,
            includedInRate = false,
        });
        var expense = await res.Content.ReadFromJsonAsync<IdRef>();
        var storage = (InMemoryFileStorage)factory.Services
            .GetRequiredService<IFileStorage>();
        var before = storage.Count;
        await Admin.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={expense!.Id}",
            Upload("kvitto.png", "image/png", Png()));
        Assert.Equal(before + 1, storage.Count);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.DeleteAsync($"/api/expenses/{expense.Id}")).StatusCode);
        Assert.Equal(before, storage.Count);
    }

    [Fact]
    public async Task Receipt_Upload_Locked_Once_Timesheet_Submitted()
    {
        await LinkedResourceAsync();
        var expense = await PostExpenseAsync(Consultant, "2027-11-01"); // ISO W45
        var (year, week) = (ISOWeek.GetYear(new DateOnly(2027, 11, 1).ToDateTime(TimeOnly.MinValue)),
            ISOWeek.GetWeekOfYear(new DateOnly(2027, 11, 1).ToDateTime(TimeOnly.MinValue)));
        var submit = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year, isoWeek = week });
        submit.EnsureSuccessStatusCode();

        var up = await Consultant.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={expense.Id}",
            Upload("kvitto.png", "image/png", Png()));
        Assert.Equal(HttpStatusCode.Conflict, up.StatusCode);
    }
}
