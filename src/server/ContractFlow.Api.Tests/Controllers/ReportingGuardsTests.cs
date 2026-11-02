using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Integrity guards around execution reporting: rows can't be stranded in a
/// submitted week, moved out of a closed month, duplicated (expense warning,
/// one per-diem per day) or read/edited across resources; booking evidence
/// survives; billable quantities stay out of the Resource view.
/// </summary>
public class ReportingGuardsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    private static readonly Guid LinkedId = Guid.Parse("00000000-0000-4000-8000-0000000000ab");

    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private async Task<Scaffold.Ids> GraphAsync(string tag)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == LinkedId))
            {
                db.Resources.Add(new Resource
                {
                    Id = LinkedId, DisplayName = "Guard Tester",
                    Type = ResourceType.Subcontractor, UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        return ids;
    }

    private Task CloseMonthAsync(Guid projectId, int year, int month) =>
        factory.SeedAsync(async db =>
        {
            db.ProjectMonthCloses.Add(new ProjectMonthClose { ProjectId = projectId, Year = year, Month = month });
            await db.SaveChangesAsync();
        });

    private async Task<Guid> PostEntryAsync(Guid projectId, string date, decimal hours)
    {
        var res = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId, date, category = "work", actualHours = hours,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    private async Task SubmitWeekAsync(DateOnly day)
    {
        var dt = day.ToDateTime(TimeOnly.MinValue);
        var res = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year = ISOWeek.GetYear(dt), isoWeek = ISOWeek.GetWeekOfYear(dt) });
        res.EnsureSuccessStatusCode();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    // ── Submitted weeks ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_IntoSubmittedWeek_ConflictsForEveryRowType()
    {
        var ids = await GraphAsync("weeklock");
        await PostEntryAsync(ids.ProjectId, "2028-03-06", 8m);
        await SubmitWeekAsync(new DateOnly(2028, 3, 6));

        var entry = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2028-03-07", actualHours = 4m,
        });
        var travel = await Consultant.PostAsJsonAsync("/api/travel-records", new
        {
            projectId = ids.ProjectId, date = "2028-03-07", mode = "car", distanceKm = 10m,
        });
        var expense = await Consultant.PostAsJsonAsync("/api/expenses", new
        {
            projectId = ids.ProjectId, date = "2028-03-07", amount = 50m, currency = "SEK",
            reimbursableToResource = true,
        });
        var allowance = await Consultant.PostAsJsonAsync("/api/allowances", new
        {
            projectId = ids.ProjectId, date = "2028-03-07", amount = 5m, currency = "SEK",
        });

        foreach (var res in new[] { entry, travel, expense, allowance })
        {
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal("weekLocked", await ErrorCodeAsync(res));
        }

        // The following week is still open.
        var next = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2028-03-13", actualHours = 4m,
        });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task Update_MovingDraftIntoSubmittedWeek_Conflicts()
    {
        var ids = await GraphAsync("weekmove");
        await PostEntryAsync(ids.ProjectId, "2028-03-20", 8m);
        await SubmitWeekAsync(new DateOnly(2028, 3, 20));
        var draft = await PostEntryAsync(ids.ProjectId, "2028-03-27", 8m);

        var moved = await Consultant.PutAsJsonAsync($"/api/time-entries/{draft}", new
        {
            projectId = ids.ProjectId, date = "2028-03-21", actualHours = 8m,
        });
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal("weekLocked", await ErrorCodeAsync(moved));
    }

    // ── Closed months ───────────────────────────────────────────────────────

    [Fact]
    public async Task DraftRow_CannotLeaveOrBeDeletedFromClosedMonth()
    {
        var ids = await GraphAsync("closedmove");
        var entry = await PostEntryAsync(ids.ProjectId, "2028-04-28", 8m);
        await CloseMonthAsync(ids.ProjectId, 2028, 4);

        var moved = await Consultant.PutAsJsonAsync($"/api/time-entries/{entry}", new
        {
            projectId = ids.ProjectId, date = "2028-05-02", actualHours = 8m,
        });
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal("monthClosed", await ErrorCodeAsync(moved));

        var deleted = await Consultant.DeleteAsync($"/api/time-entries/{entry}");
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
    }

    // ── Day plausibility ────────────────────────────────────────────────────

    [Fact]
    public async Task Entries_ForOneDay_CannotExceed24Hours()
    {
        var ids = await GraphAsync("overflow");
        await PostEntryAsync(ids.ProjectId, "2028-05-08", 16m);

        var over = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2028-05-08", actualHours = 9m,
        });
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Equal("dayOverflow", await ErrorCodeAsync(over));

        var fits = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2028-05-08", actualHours = 8m,
        });
        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
    }

    // ── Duplicates ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Expense_SameDayAmountCurrency_WarnsUntilConfirmed()
    {
        var ids = await GraphAsync("expdupe");
        var body = new
        {
            projectId = ids.ProjectId, date = "2028-05-15", amount = 1200m, currency = "SEK",
            reimbursableToResource = true, confirmDuplicate = false,
        };
        var first = await Consultant.PostAsJsonAsync("/api/expenses", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var dupe = await Consultant.PostAsJsonAsync("/api/expenses", body);
        Assert.Equal(HttpStatusCode.Conflict, dupe.StatusCode);
        var warning = await dupe.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("possibleDuplicate", warning.GetProperty("error").GetString());
        Assert.Equal(firstId, warning.GetProperty("duplicates")[0].GetGuid());

        var confirmed = await Consultant.PostAsJsonAsync("/api/expenses", body with { confirmDuplicate = true });
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);

        // A row is never its own duplicate: editing it to a distinct amount saves.
        var edit = await Consultant.PutAsJsonAsync($"/api/expenses/{firstId}", body with
        {
            amount = 1100m,
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }

    [Fact]
    public async Task Expense_RechargeableWithoutProject_BadRequest()
    {
        await GraphAsync("exprecharge");
        var res = await Consultant.PostAsJsonAsync("/api/expenses", new
        {
            date = "2028-05-16", amount = 100m, currency = "SEK", rechargeableToCustomer = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var ok = await Consultant.PostAsJsonAsync("/api/expenses", new
        {
            date = "2028-05-16", amount = 100m, currency = "SEK", reimbursableToResource = true,
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Allowance_UpdateOntoExistingDay_Conflicts()
    {
        var ids = await GraphAsync("perdiemmove");
        await Consultant.PostAsJsonAsync("/api/allowances", new
        {
            projectId = ids.ProjectId, date = "2028-05-22", amount = 5m, currency = "SEK",
        });
        var second = await Consultant.PostAsJsonAsync("/api/allowances", new
        {
            projectId = ids.ProjectId, date = "2028-05-23", amount = 5m, currency = "SEK",
        });
        var secondId = (await second.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var moved = await Consultant.PutAsJsonAsync($"/api/allowances/{secondId}", new
        {
            projectId = ids.ProjectId, date = "2028-05-22", amount = 5m, currency = "SEK",
        });
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal("duplicate", await ErrorCodeAsync(moved));

        var sameDay = await Consultant.PutAsJsonAsync($"/api/allowances/{secondId}", new
        {
            projectId = ids.ProjectId, date = "2028-05-23", amount = 6m, currency = "SEK",
        });
        Assert.Equal(HttpStatusCode.OK, sameDay.StatusCode);
    }

    // ── Cross-resource access ───────────────────────────────────────────────

    [Fact]
    public async Task Resource_CannotReadOrEditAnotherResourcesRows()
    {
        var ids = await GraphAsync("foreign");
        var other = ids.ResourceId; // scaffold resource — not the consultant's
        async Task<Guid> AdminPost(string url, object body)
        {
            var res = await Admin.PostAsJsonAsync(url, body);
            res.EnsureSuccessStatusCode();
            return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
        }
        var entry = await AdminPost("/api/time-entries", new
        {
            resourceId = other, projectId = ids.ProjectId, date = "2028-06-05", actualHours = 8m,
        });
        var travel = await AdminPost("/api/travel-records", new
        {
            resourceId = other, projectId = ids.ProjectId, date = "2028-06-05", mode = "car",
        });
        var expense = await AdminPost("/api/expenses", new
        {
            resourceId = other, projectId = ids.ProjectId, date = "2028-06-05",
            amount = 10m, currency = "SEK", reimbursableToResource = true,
        });
        var allowance = await AdminPost("/api/allowances", new
        {
            resourceId = other, projectId = ids.ProjectId, date = "2028-06-05",
            amount = 5m, currency = "SEK",
        });

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync($"/api/time-entries/{entry}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.PutAsJsonAsync(
            $"/api/time-entries/{entry}",
            new { projectId = ids.ProjectId, date = "2028-06-05", actualHours = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.DeleteAsync($"/api/time-entries/{entry}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.PutAsJsonAsync(
            $"/api/travel-records/{travel}",
            new { projectId = ids.ProjectId, date = "2028-06-05", mode = "car" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.DeleteAsync($"/api/travel-records/{travel}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.PutAsJsonAsync(
            $"/api/expenses/{expense}",
            new { date = "2028-06-05", amount = 1m, currency = "SEK" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.DeleteAsync($"/api/expenses/{expense}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.PutAsJsonAsync(
            $"/api/allowances/{allowance}",
            new { date = "2028-06-05", amount = 1m, currency = "SEK" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.DeleteAsync($"/api/allowances/{allowance}")).StatusCode);

        // Lists are scoped, not refused: none of the other resource's rows show.
        foreach (var url in new[] { "/api/travel-records", "/api/expenses", "/api/allowances" })
        {
            var rows = await Consultant.GetFromJsonAsync<JsonElement[]>($"{url}?from=2028-06-05&to=2028-06-05");
            Assert.Empty(rows!);
        }

        // Allowed path: admin edits the same row.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.DeleteAsync($"/api/allowances/{allowance}")).StatusCode);
    }

    [Fact]
    public async Task Resource_SeesNoBillableHours_AdminDoes()
    {
        var ids = await GraphAsync("billable");
        var entry = await PostEntryAsync(ids.ProjectId, "2028-06-12", 3m);
        await factory.SeedAsync(async db =>
        {
            var row = await db.TimeEntries.FirstAsync(e => e.Id == entry);
            row.BillableHours = 8m;
            await db.SaveChangesAsync();
        });

        var mine = await Consultant.GetFromJsonAsync<JsonElement>($"/api/time-entries/{entry}");
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("billableHours").ValueKind);
        var admin = await Admin.GetFromJsonAsync<JsonElement>($"/api/time-entries/{entry}");
        Assert.Equal(8m, admin.GetProperty("billableHours").GetDecimal());
    }

    [Fact]
    public async Task GatedProjectPackages_UnlistedResource403_ListedResource200()
    {
        var ids = await GraphAsync("gated");
        var grantOther = await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/access",
            new { resourceId = ids.ResourceId });
        Assert.True(grantOther.IsSuccessStatusCode);

        var denied = await Consultant.GetAsync($"/api/projects/{ids.ProjectId}/work-packages");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await Admin.PostAsJsonAsync($"/api/projects/{ids.ProjectId}/access", new { resourceId = LinkedId });
        var allowed = await Consultant.GetAsync($"/api/projects/{ids.ProjectId}/work-packages");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ── Mobilisation integrity ──────────────────────────────────────────────

    [Fact]
    public async Task Mobilisation_SplitKeepsPackage_SelfMergeRejected()
    {
        var ids = await GraphAsync("mobguard");
        var package = await (await Admin.PostAsJsonAsync("/api/work-packages", new
        {
            projectId = ids.ProjectId, name = "Package North",
        })).Content.ReadFromJsonAsync<IdRef>();
        var confirm = await Admin.PostAsJsonAsync("/api/mobilisations", new
        {
            projectId = ids.ProjectId, resourceId = LinkedId,
            startDate = "2028-06-19", endDate = "2028-06-23", workPackageId = package!.Id,
        });
        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
        var periodId = (await confirm.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var self = await Admin.PostAsJsonAsync($"/api/mobilisations/{periodId}/merge",
            new { otherId = periodId });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        var split = await Admin.PostAsJsonAsync($"/api/mobilisations/{periodId}/split",
            new { fromDate = "2028-06-21" });
        Assert.Equal(HttpStatusCode.OK, split.StatusCode);
        var body = await split.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(package.Id, body.GetProperty("first").GetProperty("workPackageId").GetGuid());
        Assert.Equal(package.Id, body.GetProperty("second").GetProperty("workPackageId").GetGuid());
    }

    // ── Booking evidence ────────────────────────────────────────────────────

    [Fact]
    public async Task BookingEvidence_ResourceCannotDeleteAdminUpload_AdminCan()
    {
        var ids = await GraphAsync("evidence");
        var booking = await Scaffold.CreateBookingAsync(Admin, ids with { ResourceId = LinkedId }, "2028-06-26");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4 evidence"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "instruction.pdf");
        var upload = await Admin.PostAsync(
            $"/api/documents?ownerType=booking&ownerId={booking.Id}", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var docId = (await upload.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var byResource = await Consultant.DeleteAsync($"/api/documents/{docId}");
        Assert.Equal(HttpStatusCode.Forbidden, byResource.StatusCode);
        var byAdmin = await Admin.DeleteAsync($"/api/documents/{docId}");
        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
    }

    [Fact]
    public async Task Upload_ContentNotMatchingDeclaredType_BadRequest()
    {
        var ids = await GraphAsync("sniff");
        var expense = await (await Consultant.PostAsJsonAsync("/api/expenses", new
        {
            projectId = ids.ProjectId, date = "2028-07-03", amount = 10m, currency = "SEK",
            reimbursableToResource = true,
        })).Content.ReadFromJsonAsync<IdRef>();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x4D, 0x5A, 0x90, 0x00]); // an executable header
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "receipt.png");
        var res = await Consultant.PostAsync(
            $"/api/documents?ownerType=expense&ownerId={expense!.Id}", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
