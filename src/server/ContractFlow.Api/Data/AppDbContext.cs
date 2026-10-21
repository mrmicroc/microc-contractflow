using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Data;

/// <param name="auditActor">Resolves who is saving for the audit trail;
/// absent (design-time tooling) ⇒ rows are recorded as system writes.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options, IAuditActor? auditActor = null)
    : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ScheduledCharge> ScheduledCharges => Set<ScheduledCharge>();
    public DbSet<ContractTerms> ContractTerms => Set<ContractTerms>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkPackage> WorkPackages => Set<WorkPackage>();
    public DbSet<WorkPackageInfo> WorkPackageInfos => Set<WorkPackageInfo>();
    public DbSet<WorkPackageRate> WorkPackageRates => Set<WorkPackageRate>();
    public DbSet<ProjectResource> ProjectResources => Set<ProjectResource>();
    public DbSet<ProjectAccess> ProjectAccesses => Set<ProjectAccess>();
    public DbSet<EstimatedQuantity> EstimatedQuantities => Set<EstimatedQuantity>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceLeave> ResourceLeaves => Set<ResourceLeave>();
    public DbSet<ResourceCompetency> ResourceCompetencies => Set<ResourceCompetency>();
    public DbSet<CompensationAgreement> CompensationAgreements => Set<CompensationAgreement>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<PadTransaction> PadTransactions => Set<PadTransaction>();
    public DbSet<AdditionalPadRequest> AdditionalPadRequests => Set<AdditionalPadRequest>();
    public DbSet<ProjectMonthClose> ProjectMonthCloses => Set<ProjectMonthClose>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<TravelRecord> TravelRecords => Set<TravelRecord>();
    public DbSet<MobilisationPeriod> MobilisationPeriods => Set<MobilisationPeriod>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<AllowanceClaim> AllowanceClaims => Set<AllowanceClaim>();
    public DbSet<Timesheet> Timesheets => Set<Timesheet>();
    public DbSet<TimesheetReview> TimesheetReviews => Set<TimesheetReview>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<InvoiceSpecification> InvoiceSpecifications => Set<InvoiceSpecification>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<SubcontractorStatement> SubcontractorStatements => Set<SubcontractorStatement>();
    public DbSet<StatementLine> StatementLines => Set<StatementLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Optional justification stamped on the audit rows of the next
    /// save — set by an endpoint that takes a reason; cleared after saving.</summary>
    public string? AuditReason { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AppendAuditAsync(CancellationToken.None).GetAwaiter().GetResult();
        try { return base.SaveChanges(acceptAllChangesOnSuccess); }
        finally { AuditReason = null; }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await AppendAuditAsync(cancellationToken);
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        finally { AuditReason = null; }
    }

    /// <summary>Adds AuditEntry rows for the pending changes — same save, same
    /// transaction, so a change is never persisted without its audit row.</summary>
    private async Task AppendAuditAsync(CancellationToken ct)
    {
        ChangeTracker.DetectChanges();
        var rows = AuditTrail.Collect(ChangeTracker);
        if (rows.Count == 0) return;

        var actor = auditActor is null ? null : await auditActor.ResolveAsync(ct);
        foreach (var row in rows)
        {
            row.UserId = actor?.Id;
            row.UserDisplayName = actor?.DisplayName;
            row.Reason = AuditReason;
        }
        AuditEntries.AddRange(rows);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Customer>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.ExternalReference).HasMaxLength(100);
            e.Property(c => c.VatNumber).HasMaxLength(50);
            e.Property(c => c.AddressLine).HasMaxLength(200);
            e.Property(c => c.PostalCode).HasMaxLength(20);
            e.Property(c => c.City).HasMaxLength(100);
            e.Property(c => c.Country).HasMaxLength(2);
            // Contact list travels inside the customer row as JSON.
            e.OwnsMany(c => c.Contacts, s =>
            {
                s.ToJson();
                s.Property(x => x.Name).HasMaxLength(200);
                s.Property(x => x.Role).HasMaxLength(100);
                s.Property(x => x.Email).HasMaxLength(320);
                s.Property(x => x.Phone).HasMaxLength(50);
            });
        });

        builder.Entity<Contract>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.ExternalReference).HasMaxLength(100);
            e.Property(c => c.EndClientReference).HasMaxLength(100);
            e.HasOne(c => c.Customer).WithMany(cu => cu.Contracts)
                .HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
            // Same register as the party; an organisation in use as an end
            // client can't be deleted (Restrict — no second cascade path).
            e.HasOne(c => c.EndClient).WithMany(cu => cu.EndClientContracts)
                .HasForeignKey(c => c.EndClientId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(c => c.Representatives, r =>
            {
                r.ToJson();
                r.Property(x => x.Name).HasMaxLength(200);
                r.Property(x => x.Email).HasMaxLength(320);
                r.Property(x => x.Phone).HasMaxLength(50);
                r.Property(x => x.Authority).HasMaxLength(300);
            });
        });

        builder.Entity<ScheduledCharge>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(300);
            e.HasIndex(x => new { x.ContractId, x.Date });
            e.HasOne(x => x.Contract).WithMany(c => c.ScheduledCharges)
                .HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            // A project with charges can't silently orphan them (no second cascade path).
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ContractTerms>(e =>
        {
            e.HasIndex(t => new { t.ContractId, t.Version }).IsUnique();
            e.HasIndex(t => new { t.ContractId, t.EffectiveFrom });
            e.HasOne(t => t.Contract).WithMany(c => c.TermsVersions)
                .HasForeignKey(t => t.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Currency).HasMaxLength(3);
            e.Property(t => t.HolidayCalendar).HasMaxLength(20);
            e.Property(t => t.TimeBilling).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.PadRate).HasPrecision(18, 2);
            e.Property(t => t.DayRate).HasPrecision(18, 2);
            e.Property(t => t.SaturdayDayRate).HasPrecision(18, 2);
            e.Property(t => t.SundayDayRate).HasPrecision(18, 2);
            e.Property(t => t.HolidayDayRate).HasPrecision(18, 2);
            e.Property(t => t.HalfDayRate).HasPrecision(18, 2);
            e.Property(t => t.HalfDayMaxHours).HasPrecision(9, 2);
            // The cancellation ladder travels inside the version row as JSON —
            // versioning then covers policy changes for free.
            e.OwnsMany(t => t.CancellationSteps, s =>
            {
                s.ToJson();
                s.Property(x => x.ChargeHours).HasPrecision(9, 2);
                s.Property(x => x.ChargeAmount).HasPrecision(18, 2);
                s.Property(x => x.ChargeDayFraction).HasPrecision(9, 4);
            });
            e.Property(t => t.HourlyRate).HasPrecision(18, 2);
            e.Property(t => t.SaturdayRate).HasPrecision(18, 2);
            e.Property(t => t.SundayRate).HasPrecision(18, 2);
            e.Property(t => t.HolidayRate).HasPrecision(18, 2);
            e.Property(t => t.MobilisationFee).HasPrecision(18, 2);
            e.Property(t => t.OvertimePercent).HasPrecision(9, 4);
            e.Property(t => t.ExpenseMarkupPercent).HasPrecision(9, 4);
            e.Property(t => t.PadHoursPerDay).HasPrecision(9, 4);
            e.Property(t => t.MinPadPerBookedDay).HasPrecision(9, 4);
            e.Property(t => t.CarryOverMaxPercent).HasPrecision(9, 4);
            e.Property(t => t.MinBillableHoursPerDay).HasPrecision(9, 2);
        });

        builder.Entity<Project>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Code).HasMaxLength(50);
            e.Property(p => p.ContractedBaselinePad).HasPrecision(18, 4);
            e.HasOne(p => p.Contract).WithMany(c => c.Projects)
                .HasForeignKey(p => p.ContractId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkPackageInfo>(e =>
        {
            e.Property(i => i.Type).HasMaxLength(60);
            e.Property(i => i.Description).HasMaxLength(500);
            e.HasOne(i => i.WorkPackage).WithMany(w => w.InfoItems)
                .HasForeignKey(i => i.WorkPackageId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkPackage>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(200);
            e.Property(l => l.PricingMode).HasConversion<string>().HasMaxLength(20);
            e.HasOne(l => l.Project).WithMany(p => p.WorkPackages)
                .HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkPackageRate>(e =>
        {
            e.HasIndex(r => new { r.WorkPackageId, r.ResourceId, r.EffectiveFrom });
            e.HasOne(r => r.WorkPackage).WithMany(l => l.Rates)
                .HasForeignKey(r => r.WorkPackageId).OnDelete(DeleteBehavior.Cascade);
            // Rate rows anchor billing history — never let a resource delete
            // silently downgrade a matrix cell to a package-wide rate.
            e.HasOne(r => r.Resource).WithMany()
                .HasForeignKey(r => r.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.Property(r => r.HourlyRate).HasPrecision(18, 2);
            e.Property(r => r.SaturdayRate).HasPrecision(18, 2);
            e.Property(r => r.SundayRate).HasPrecision(18, 2);
            e.Property(r => r.HolidayRate).HasPrecision(18, 2);
            e.Property(r => r.DayRate).HasPrecision(18, 2);
            e.Property(r => r.SaturdayDayRate).HasPrecision(18, 2);
            e.Property(r => r.SundayDayRate).HasPrecision(18, 2);
            e.Property(r => r.HolidayDayRate).HasPrecision(18, 2);
            e.Property(r => r.HalfDayRate).HasPrecision(18, 2);
            e.Property(r => r.HalfDayMaxHours).HasPrecision(9, 2);
            e.Property(r => r.MobilisationFee).HasPrecision(18, 2);
            e.Property(r => r.Notes).HasMaxLength(500);
        });

        builder.Entity<ProjectResource>(e =>
        {
            e.HasIndex(r => new { r.ProjectId, r.ResourceId }).IsUnique();
            e.HasOne(r => r.Project).WithMany()
                .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Resource).WithMany()
                .HasForeignKey(r => r.ResourceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProjectAccess>(e =>
        {
            e.HasIndex(a => new { a.ProjectId, a.ResourceId }).IsUnique();
            e.HasOne(a => a.Project).WithMany()
                .HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Resource).WithMany()
                .HasForeignKey(a => a.ResourceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EstimatedQuantity>(e =>
        {
            e.Property(q => q.Label).HasMaxLength(200);
            e.Property(q => q.Unit).HasMaxLength(30);
            e.Property(q => q.Quantity).HasPrecision(18, 4);
            e.HasOne(q => q.Project).WithMany(p => p.EstimatedQuantities)
                .HasForeignKey(q => q.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Resource>(e =>
        {
            e.Property(r => r.DisplayName).HasMaxLength(200);
            e.Property(r => r.Email).HasMaxLength(320);
            e.Property(r => r.Phone).HasMaxLength(50);
            e.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);
            e.HasOne(r => r.User).WithMany()
                .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ResourceLeave>(e =>
        {
            e.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne(l => l.Resource).WithMany(r => r.Leave)
                .HasForeignKey(l => l.ResourceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ResourceCompetency>(e =>
        {
            e.Property(c => c.Label).HasMaxLength(200);
            e.HasOne(c => c.Resource).WithMany(r => r.CompetencyItems)
                .HasForeignKey(c => c.ResourceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CompensationAgreement>(e =>
        {
            // Version is unique per resource + scope — two filtered indexes,
            // one per scope kind (contract-scope rows have WorkPackageId null)
            e.HasIndex(a => new { a.ResourceId, a.ContractId, a.Version }).IsUnique()
                .HasFilter("[ContractId] IS NOT NULL AND [WorkPackageId] IS NULL");
            e.HasIndex(a => new { a.ResourceId, a.ContractId, a.WorkPackageId, a.Version }).IsUnique()
                .HasFilter("[WorkPackageId] IS NOT NULL");
            e.HasIndex(a => new { a.ResourceId, a.EffectiveFrom });
            e.HasOne(a => a.Resource).WithMany(r => r.Agreements)
                .HasForeignKey(a => a.ResourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Contract).WithMany()
                .HasForeignKey(a => a.ContractId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.WorkPackage).WithMany()
                .HasForeignKey(a => a.WorkPackageId).OnDelete(DeleteBehavior.SetNull);
            e.Property(a => a.Currency).HasMaxLength(3);
            e.Property(a => a.PerDiem).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.HourlyRate).HasPrecision(18, 2);
            e.Property(a => a.SaturdayRate).HasPrecision(18, 2);
            e.Property(a => a.SundayRate).HasPrecision(18, 2);
            e.Property(a => a.HolidayRate).HasPrecision(18, 2);
            e.Property(a => a.MileageRate).HasPrecision(18, 2);
            e.Property(a => a.PerDiemAmount).HasPrecision(18, 2);
            e.Property(a => a.OvertimePercent).HasPrecision(9, 4);
            e.Property(a => a.TravelTimePercent).HasPrecision(9, 4);
            e.Property(a => a.MinPayableHoursPerDay).HasPrecision(9, 2);
        });

        builder.Entity<Booking>(e =>
        {
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(b => b.PlannedHours).HasPrecision(9, 4);
            e.Property(b => b.CancellationChargeHours).HasPrecision(9, 4);
            e.Property(b => b.CancellationChargeAmount).HasPrecision(18, 2);
            e.Property(b => b.CancellationChargeDayFraction).HasPrecision(9, 4);
            e.Property(b => b.InstructedBy).HasMaxLength(200);
            e.Property(b => b.ActivityRef).HasMaxLength(100);
            e.Property(b => b.EvidenceRef).HasMaxLength(200);
            e.Property(b => b.CustomerContact).HasMaxLength(200);
            e.HasIndex(b => new { b.ProjectId, b.Date });
            e.HasIndex(b => new { b.ResourceId, b.Date });
            e.HasOne(b => b.Project).WithMany()
                .HasForeignKey(b => b.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Resource).WithMany()
                .HasForeignKey(b => b.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.WorkPackage).WithMany()
                .HasForeignKey(b => b.WorkPackageId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PadTransaction>(e =>
        {
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(t => t.Amount).HasPrecision(9, 4);
            e.HasIndex(t => new { t.ProjectId, t.Year, t.Month });
            e.HasOne(t => t.Project).WithMany()
                .HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Booking).WithMany()
                .HasForeignKey(t => t.BookingId).OnDelete(DeleteBehavior.SetNull);
            // One commitment per booking and one approval row per request —
            // a concurrent double confirm/approve fails here instead of
            // reserving PAD twice.
            e.HasIndex(t => t.BookingId).IsUnique()
                .HasFilter("[Type] = 'Commit' AND [BookingId] IS NOT NULL")
                .HasDatabaseName("IX_PadTransactions_BookingId_Commit");
            e.HasIndex(t => t.AdditionalPadRequestId).IsUnique()
                .HasFilter("[Type] = 'AdditionalApproved' AND [AdditionalPadRequestId] IS NOT NULL")
                .HasDatabaseName("IX_PadTransactions_AdditionalPadRequestId_Approved");
        });

        builder.Entity<AdditionalPadRequest>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Quantity).HasPrecision(9, 4);
            e.Property(r => r.Rate).HasPrecision(18, 2);
            e.Property(r => r.Reference).HasMaxLength(200);
            e.Property(r => r.ApprovedByRepresentative).HasMaxLength(200);
            e.HasOne(r => r.Project).WithMany()
                .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Booking).WithMany()
                .HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UserDisplayName).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.OccurredUtc);
        });

        builder.Entity<ProjectMonthClose>(e =>
        {
            e.Property(c => c.ForcedReason).HasMaxLength(500);
            e.HasIndex(c => new { c.ProjectId, c.Year, c.Month }).IsUnique();
            e.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TimeEntry>(e =>
        {
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Billing).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ActualHours).HasPrecision(9, 2);
            e.Property(x => x.PayableHours).HasPrecision(9, 2);
            e.Property(x => x.BillableHours).HasPrecision(9, 2);
            e.Property(x => x.MinimumAdjustmentHours).HasPrecision(9, 2);
            e.Property(x => x.BillableMinimumAdjustmentHours).HasPrecision(9, 2);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.InvoiceSpecificationId);
            e.HasOne<InvoiceSpecification>().WithMany()
                .HasForeignKey(x => x.InvoiceSpecificationId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.ResourceId, x.Date });
            e.HasIndex(x => new { x.ProjectId, x.Date });
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.WorkPackage).WithMany()
                .HasForeignKey(x => x.WorkPackageId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Timesheet).WithMany(t => t.Entries)
                .HasForeignKey(x => x.TimesheetId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TravelRecord>(e =>
        {
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FromPlace).HasMaxLength(200);
            e.Property(x => x.ToPlace).HasMaxLength(200);
            e.Property(x => x.DistanceKm).HasPrecision(9, 2);
            e.Property(x => x.DurationHours).HasPrecision(9, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.ResourceId, x.Date });
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.MobilisationPeriod).WithMany()
                .HasForeignKey(x => x.MobilisationPeriodId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Timesheet).WithMany(t => t.TravelRecords)
                .HasForeignKey(x => x.TimesheetId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MobilisationPeriod>(e =>
        {
            e.HasIndex(x => new { x.ResourceId, x.StartDate });
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WorkPackage).WithMany()
                .HasForeignKey(x => x.WorkPackageId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Expense>(e =>
        {
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.ReceiptRef).HasMaxLength(300);
            e.HasIndex(x => new { x.ResourceId, x.Date });
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Timesheet).WithMany(t => t.Expenses)
                .HasForeignKey(x => x.TimesheetId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AllowanceClaim>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Notes).HasMaxLength(300);
            e.HasIndex(x => new { x.ResourceId, x.Date });
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Timesheet).WithMany(t => t.AllowanceClaims)
                .HasForeignKey(x => x.TimesheetId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Timesheet>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReviewerNote).HasMaxLength(500);
            e.HasIndex(x => new { x.ResourceId, x.Year, x.IsoWeek }).IsUnique();
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Reviews).WithOne(r => r.Timesheet)
                .HasForeignKey(r => r.TimesheetId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TimesheetReview>(e =>
        {
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.TimesheetId, x.AtUtc });
        });

        builder.Entity<Document>(e =>
        {
            e.Property(x => x.OwnerType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.StorageKey).HasMaxLength(300);
            e.Property(x => x.Label).HasMaxLength(200);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });

        builder.Entity<InvoiceSpecification>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.WarningsJson).HasMaxLength(4000);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.EndClientName).HasMaxLength(200);
            e.Property(x => x.EndClientReference).HasMaxLength(100);
            e.HasIndex(x => new { x.ProjectId, x.Year, x.Month, x.Revision }).IsUnique();
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InvoiceLine>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Explanation).HasMaxLength(500);
            // 4 decimals: day-rate lines bill fractions like ⅓ day (0.3333).
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Invoice).WithMany(x => x.Lines)
                .HasForeignKey(x => x.InvoiceSpecificationId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SubcontractorStatement>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.WarningsJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.ResourceId, x.Year, x.Month }).IsUnique();
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StatementLine>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Explanation).HasMaxLength(500);
            e.Property(x => x.Quantity).HasPrecision(18, 2);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Statement).WithMany(x => x.Lines)
                .HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SupplierInvoice>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Number).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.MatchDiffJson).HasMaxLength(4000);
            e.Property(x => x.ApprovalNote).HasMaxLength(500);
            e.HasIndex(x => new { x.ResourceId, x.Number }).IsUnique();
            e.HasOne(x => x.Resource).WithMany()
                .HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Statement).WithMany()
                .HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SupplierInvoiceLine>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Invoice).WithMany(x => x.Lines)
                .HasForeignKey(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
