using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

/// <summary>Entity→DTO mapping for the execution-reporting rows — shared by
/// the entry controllers and the timesheet detail view.</summary>
public static class ReportingMapper
{
    /// <param name="hideBillable">True for Resource-role callers — billable
    /// quantities are customer-side (§1.1/§10) and never shown to them.</param>
    public static TimeEntryDto Entry(TimeEntry x, bool hideBillable) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project.Name,
        x.BookingId, x.WorkPackageId, x.WorkPackage?.Name, x.Date, x.Category, x.ActualHours,
        x.Billing, x.Description, x.PayableHours, hideBillable ? null : x.BillableHours,
        x.MinimumAdjustmentHours, x.TimesheetId, x.Timesheet?.Status,
        hideBillable ? null : x.BillableMinimumAdjustmentHours);

    public static TimeEntryDto Entry(TimeEntry x) => Entry(x, hideBillable: false);

    public static TravelRecordDto Travel(TravelRecord x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project?.Name,
        x.BookingId, x.Date, x.FromPlace, x.ToPlace, x.Mode, x.DistanceKm,
        x.DurationHours, x.MobilisationPeriodId, x.Notes, x.TimesheetId);

    public static ExpenseDto Expense(Expense x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project?.Name,
        x.BookingId, x.Date, x.Category, x.Amount, x.Currency, x.Description,
        x.ReceiptRef, x.ReimbursableToResource, x.RechargeableToCustomer,
        x.IncludedInRate, x.TimesheetId);

    public static AllowanceClaimDto Allowance(AllowanceClaim x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project?.Name,
        x.Date, x.Amount, x.Currency, x.Notes, x.TimesheetId);

    public static TimesheetDto Sheet(Timesheet t) => new(
        t.Id, t.ResourceId, t.Resource.DisplayName, t.Year, t.IsoWeek, t.Status,
        t.SubmittedUtc, t.ReviewedUtc, t.ReviewerNote,
        t.Entries.Count, t.TravelRecords.Count, t.Expenses.Count,
        t.AllowanceClaims.Count,
        t.Entries.Where(e => e.Category != TimeEntryCategory.Break)
            .Sum(e => e.ActualHours));

    public static MobilisationPeriodDto Mobilisation(MobilisationPeriod p) => new(
        p.Id, p.ProjectId, p.Project.Name, p.ResourceId, p.Resource.DisplayName,
        p.StartDate, p.EndDate, p.ConfirmedUtc, p.WorkPackageId, p.WorkPackage?.Name);
}
