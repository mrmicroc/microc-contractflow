using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Phase 3 money: invoice specifications + subcontractor statements generated
/// from approved timesheets (design §6.1/§6.4). Billing runs on contract
/// rates, compensation on agreement rates — the same approved rows must price
/// differently on each side (the worked-example totals below assert exactly
/// that).
/// </summary>
public class BillingTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);
    public record InvoiceDto(Guid Id, string Status, decimal TotalAmount, int LineCount);
    public record LineDto(Guid Id, string Kind, string? Date, string Description,
        string? Explanation, decimal Quantity, decimal UnitPrice, decimal Amount);
    public record InvoiceDetail(InvoiceDto Invoice, LineDto[] Lines);
    public record StatementDto(Guid Id, string Status, decimal TotalAmount, int LineCount);
    public record StatementDetail(StatementDto Statement, LineDto[] Lines);
    public record SheetDto(Guid Id, string Status);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);

    /// <summary>Graph + rated terms + rated agreement for the linked resource.</summary>
    private async Task<Scaffold.Ids> RatedGraphAsync(string tag)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        var linked = TimeReportingTests.LinkedId;
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == linked))
            {
                db.Resources.Add(new Resource
                {
                    Id = linked, DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor, UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            hourlyRate = 1000m, expenseMarkupPercent = 10m, mobilisationFee = 500m,
        });
        await Admin.PostAsJsonAsync($"/api/resources/{linked}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK",
            hourlyRate = 600m, mileageRate = 25m, travelTimePercent = 50m,
        });
        return ids;
    }

    /// <summary>One approved week for the linked resource on the project:
    /// 8 h Monday + rechargeable/reimbursable expense 1 200 + 100 km / 2 h
    /// travel + 350 per diem.</summary>
    private async Task<SheetDto> ApprovedWeekAsync(Scaffold.Ids ids, string monday)
    {
        async Task Post(string url, object body) =>
            (await Consultant.PostAsJsonAsync(url, body)).EnsureSuccessStatusCode();
        await Post("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = monday, category = "work", actualHours = 8m,
        });
        await Post("/api/expenses", new
        {
            projectId = ids.ProjectId, date = monday, category = "material",
            amount = 1200m, currency = "SEK", description = "Cable",
            reimbursableToResource = true, rechargeableToCustomer = true, includedInRate = false,
        });
        await Post("/api/travel-records", new
        {
            projectId = ids.ProjectId, date = monday, mode = "car",
            distanceKm = 100m, durationHours = 2m,
        });
        await Post("/api/allowances", new
        {
            projectId = ids.ProjectId, date = monday, amount = 350m, currency = "SEK",
        });

        var d = DateOnly.Parse(monday);
        var (year, week) = (ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue)),
            ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)));
        var submit = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year, isoWeek = week });
        submit.EnsureSuccessStatusCode();
        var detail = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var sheetId = detail.GetProperty("sheet").GetProperty("id").GetGuid();
        var approve = await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null);
        approve.EnsureSuccessStatusCode();
        return new SheetDto(sheetId, "approved");
    }

    // ── Invoice specification ───────────────────────────────────────────────

    [Fact]
    public async Task Invoice_Generates_Lines_From_Approved_Timesheet()
    {
        var ids = await RatedGraphAsync("inv-gen");
        await ApprovedWeekAsync(ids, "2028-01-03"); // Mon, Jan 2028

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 1 });
        Assert.Equal(HttpStatusCode.OK, gen.StatusCode);
        var detail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");

        // 8 h × 1000 + expense 1200 × 1.10 = 8000 + 1320 = 9320.
        var hours = Assert.Single(detail!.Lines, l => l.Kind == "hours");
        Assert.Equal(8m, hours.Quantity);
        Assert.Equal(1000m, hours.UnitPrice);
        Assert.Equal(8000m, hours.Amount);
        var exp = Assert.Single(detail.Lines, l => l.Kind == "expense");
        Assert.Equal(1320m, exp.Amount);
        Assert.Equal(9320m, detail.Invoice.TotalAmount);
        Assert.Equal("draft", detail.Invoice.Status);
    }

    [Fact]
    public async Task Invoice_Includes_Confirmed_Mobilisation_Fee()
    {
        var ids = await RatedGraphAsync("inv-mob");
        await ApprovedWeekAsync(ids, "2028-02-07"); // Mon, Feb 2028
        await Admin.PostAsJsonAsync("/api/mobilisations", new
        {
            projectId = ids.ProjectId, resourceId = TimeReportingTests.LinkedId,
            startDate = "2028-02-07", endDate = "2028-02-08",
        });

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 2 });
        var detail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");

        var mob = Assert.Single(detail!.Lines, l => l.Kind == "mobilisation");
        Assert.Equal(500m, mob.Amount);
        Assert.Equal(8000m + 1320m + 500m, detail.Invoice.TotalAmount);
    }

    [Fact]
    public async Task Invoice_Status_Flow_And_Frozen_Regenerate()
    {
        var ids = await RatedGraphAsync("inv-flow");
        await ApprovedWeekAsync(ids, "2028-03-06");
        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 3 });
        var spec = await gen.Content.ReadFromJsonAsync<InvoiceDto>();

        // Out of order first: can't issue a draft.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsync($"/api/invoices/{spec!.Id}/issue", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/invoices/{spec.Id}/review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/invoices/{spec.Id}/finalize", null)).StatusCode);

        // Regenerating a finalized spec must not rewrite it.
        var regen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 3 });
        Assert.Equal(HttpStatusCode.Conflict, regen.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/invoices/{spec.Id}/issue", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/invoices/{spec.Id}/pay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/invoices/{spec.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invoice_Authz_Resource_Cannot_Generate_Or_List()
    {
        var ids = await RatedGraphAsync("inv-auth");
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant
            .PostAsJsonAsync("/api/invoices/generate",
                new { projectId = ids.ProjectId, year = 2028, month = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync("/api/invoices")).StatusCode);
        // Finance can generate.
        Assert.Equal(HttpStatusCode.OK, (await Finance
            .PostAsJsonAsync("/api/invoices/generate",
                new { projectId = ids.ProjectId, year = 2028, month = 1 })).StatusCode);
    }

    [Fact]
    public async Task Invoice_PadComponent_BillsUtilisedTimesPadRate()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "inv-pad");
        // PAD terms: utilised ledger rows × padRate — hours never bill twice.
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m,
            padEnabled = true, padRate = 800m,
        });
        await factory.SeedAsync(async db =>
        {
            db.PadTransactions.Add(new PadTransaction
            {
                Id = Guid.NewGuid(), ProjectId = ids.ProjectId,
                Year = 2028, Month = 8, Type = PadTransactionType.Utilise,
                Amount = 3m, Note = "test",
            });
            await db.SaveChangesAsync();
        });

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 8 });
        var detail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");

        var pad = Assert.Single(detail!.Lines, l => l.Kind == "pad");
        Assert.Equal(3m, pad.Quantity);
        Assert.Equal(800m, pad.UnitPrice);
        Assert.Equal(2400m, pad.Amount);
        Assert.Equal(2400m, detail.Invoice.TotalAmount);
    }

    [Fact]
    public async Task Invoice_PadComponent_BookedHoursDontDoubleBill()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "inv-pad2");
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m,
            padEnabled = true, padRate = 800m,
        });
        var booking = await Scaffold.CreateBookingAsync(Admin, ids, "2028-08-07");

        // An approved 8 h entry bound to the booking + 1 PAD utilised → the
        // day is inside the PAD price, so only the PAD line may appear.
        await factory.SeedAsync(async db =>
        {
            var sheet = new Timesheet
            {
                Id = Guid.NewGuid(), ResourceId = TimeReportingTests.LinkedId,
                Year = 2028, IsoWeek = 32, Status = TimesheetStatus.Approved,
            };
            db.Timesheets.Add(sheet);
            db.TimeEntries.Add(new TimeEntry
            {
                Id = Guid.NewGuid(), ResourceId = TimeReportingTests.LinkedId,
                ProjectId = ids.ProjectId, BookingId = booking.Id,
                Date = new DateOnly(2028, 8, 7),
                Category = TimeEntryCategory.Work, ActualHours = 8m,
                TimesheetId = sheet.Id,
            });
            db.PadTransactions.Add(new PadTransaction
            {
                Id = Guid.NewGuid(), ProjectId = ids.ProjectId,
                BookingId = booking.Id,
                Year = 2028, Month = 8, Type = PadTransactionType.Utilise,
                Amount = 1m,
            });
            await db.SaveChangesAsync();
        });

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 8 });
        var detail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");

        Assert.Single(detail!.Lines);
        Assert.Equal("pad", detail.Lines[0].Kind);
        Assert.Equal(800m, detail.Lines[0].Amount);
    }

    [Fact]
    public async Task Invoice_DailyRateComponent_BillsDayFractions()
    {
        var ids = await RatedGraphAsync("inv-daily");
        // Terms v2: daily-rate component — full day 8 000, half day ≤ 4 h → 5 000.
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "dailyRate", dayRate = 8000m,
            halfDayRate = 5000m, halfDayMaxHours = 4m,
            expenseMarkupPercent = 10m,
        });
        await ApprovedWeekAsync(ids, "2028-09-04"); // Monday — 8 h on site

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 9 });
        var detail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");

        // 8 h on site → 1.0 day @ 8 000 (hours never enter the price).
        var day = Assert.Single(detail!.Lines, l => l.Kind == "hours");
        Assert.Equal(1m, day.Quantity);
        Assert.Equal(8000m, day.Amount);
        Assert.Equal(8000m + 1320m, detail.Invoice.TotalAmount); // + marked-up expense
    }

    [Fact]
    public async Task Invoice_FixedPriceComponent_BillsOnItsMonth()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "inv-fp");
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "none", fixedPriceEnabled = true,
            fixedPriceAmount = 50000m, fixedPriceDate = "2028-10-15",
        });

        // The dated month carries the charge…
        var oct = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 10 });
        var octDetail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await oct.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");
        var fp = Assert.Single(octDetail!.Lines, l => l.Kind == "fixedPrice");
        Assert.Equal(50000m, fp.Amount);
        Assert.Equal(50000m, octDetail.Invoice.TotalAmount);

        // …other months don't.
        var nov = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 11 });
        var novDetail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await nov.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");
        Assert.Empty(novDetail!.Lines);
    }

    [Fact]
    public async Task Invoice_CancellationCharge_OnNonPadContract_BillsOwnLine()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "inv-cancel");
        // Hourly-only contract with a stepped ladder: ≥2 business days → 6 h.
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m,
            cancellationSteps = new[]
            {
                new { minBusinessDaysBefore = 2, chargeHours = 6m },
            },
        });

        var date = BusinessDays.AddBusinessDays(
            DateOnly.FromDateTime(DateTime.UtcNow), 2);
        var booking = await Scaffold.CreateBookingAsync(Admin, ids,
            date.ToString("yyyy-MM-dd"));
        (await Admin.PostAsync($"/api/bookings/{booking.Id}/confirm", null))
            .EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/bookings/{booking.Id}/cancel", null))
            .EnsureSuccessStatusCode();

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = date.Year, month = date.Month });
        var detail = await Admin.GetFromJsonAsync<InvoiceDetail>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<InvoiceDto>())!.Id}");

        var cancel = Assert.Single(detail!.Lines, l => l.Kind == "cancellation");
        Assert.Equal(6m, cancel.Quantity);
        Assert.Equal(1000m, cancel.UnitPrice);
        Assert.Equal(6000m, cancel.Amount);
        Assert.Equal(6000m, detail.Invoice.TotalAmount);
    }

    // ── Subcontractor statement ─────────────────────────────────────────────

    [Fact]
    public async Task Statement_Generates_Payable_Basis_From_Agreement()
    {
        var ids = await RatedGraphAsync("st-gen");
        await ApprovedWeekAsync(ids, "2028-04-03"); // Mon, Apr 2028

        var gen = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = TimeReportingTests.LinkedId, year = 2028, month = 4 });
        Assert.Equal(HttpStatusCode.OK, gen.StatusCode);
        var detail = await Admin.GetFromJsonAsync<StatementDetail>(
            $"/api/statements/{(await gen.Content.ReadFromJsonAsync<StatementDto>())!.Id}");

        // Payable side at agreement rates — NOT the customer's 1000/h:
        // hours 8×600 = 4800 · expense at cost 1200 · mileage 100×25 = 2500
        // · travel time 2 h × 50%×600 = 600 · per diem 350 → 9450.
        var hours = Assert.Single(detail!.Lines, l => l.Kind == "hours");
        Assert.Equal(600m, hours.UnitPrice);
        Assert.Equal(4800m, hours.Amount);
        Assert.Equal(1200m, Assert.Single(detail.Lines, l => l.Kind == "expense").Amount);
        Assert.Equal(2500m, Assert.Single(detail.Lines, l => l.Kind == "mileage").Amount);
        Assert.Equal(600m, Assert.Single(detail.Lines, l => l.Kind == "travelTime").Amount);
        Assert.Equal(350m, Assert.Single(detail.Lines, l => l.Kind == "allowance").Amount);
        Assert.Equal(9450m, detail.Statement.TotalAmount);
    }

    [Fact]
    public async Task Statement_Resource_Reads_Own_Only()
    {
        var ids = await RatedGraphAsync("st-auth");
        await ApprovedWeekAsync(ids, "2028-05-08");
        var gen = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = TimeReportingTests.LinkedId, year = 2028, month = 5 });
        var st = await gen.Content.ReadFromJsonAsync<StatementDto>();

        // Own statement → readable.
        Assert.Equal(HttpStatusCode.OK,
            (await Consultant.GetAsync($"/api/statements/{st!.Id}")).StatusCode);
        // But generating and other resources' statements stay internal.
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant
            .PostAsJsonAsync("/api/statements/generate",
                new { resourceId = ids.ResourceId, year = 2028, month = 5 })).StatusCode);
        var gen2 = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = ids.ResourceId, year = 2028, month = 5 });
        var other = await gen2.Content.ReadFromJsonAsync<StatementDto>();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync($"/api/statements/{other!.Id}")).StatusCode);
    }

    [Fact]
    public async Task Statement_Finalize_Pay_Flow()
    {
        var ids = await RatedGraphAsync("st-flow");
        await ApprovedWeekAsync(ids, "2028-06-05");
        var gen = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = TimeReportingTests.LinkedId, year = 2028, month = 6 });
        var st = await gen.Content.ReadFromJsonAsync<StatementDto>();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsync($"/api/statements/{st!.Id}/pay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/statements/{st.Id}/finalize", null)).StatusCode);
        // Frozen once final.
        Assert.Equal(HttpStatusCode.Conflict, (await Admin
            .PostAsJsonAsync("/api/statements/generate",
                new { resourceId = TimeReportingTests.LinkedId, year = 2028, month = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/statements/{st.Id}/pay", null)).StatusCode);
    }
}
