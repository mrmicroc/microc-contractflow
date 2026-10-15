using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<EndClient> EndClients => Set<EndClient>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractTerms> ContractTerms => Set<ContractTerms>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EstimatedQuantity> EstimatedQuantities => Set<EstimatedQuantity>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceLeave> ResourceLeaves => Set<ResourceLeave>();
    public DbSet<CompensationAgreement> CompensationAgreements => Set<CompensationAgreement>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<PadTransaction> PadTransactions => Set<PadTransaction>();
    public DbSet<AdditionalPadRequest> AdditionalPadRequests => Set<AdditionalPadRequest>();
    public DbSet<ProjectMonthClose> ProjectMonthCloses => Set<ProjectMonthClose>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Customer>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.ExternalReference).HasMaxLength(100);
        });

        builder.Entity<EndClient>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
        });

        builder.Entity<Contract>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.ExternalReference).HasMaxLength(100);
            e.Property(c => c.Model).HasConversion<string>().HasMaxLength(20);
            e.HasOne(c => c.Customer).WithMany(cu => cu.Contracts)
                .HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.EndClient).WithMany(ec => ec.Contracts)
                .HasForeignKey(c => c.EndClientId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ContractTerms>(e =>
        {
            e.HasIndex(t => new { t.ContractId, t.Version }).IsUnique();
            e.HasIndex(t => new { t.ContractId, t.EffectiveFrom });
            e.HasOne(t => t.Contract).WithMany(c => c.TermsVersions)
                .HasForeignKey(t => t.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Currency).HasMaxLength(3);
            e.Property(t => t.HolidayCalendar).HasMaxLength(20);
            e.Property(t => t.PadRate).HasPrecision(18, 2);
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
        });

        builder.Entity<Project>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Code).HasMaxLength(50);
            e.HasOne(p => p.Contract).WithMany(c => c.Projects)
                .HasForeignKey(p => p.ContractId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Location>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(200);
            e.HasOne(l => l.Project).WithMany(p => p.Locations)
                .HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
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

        builder.Entity<CompensationAgreement>(e =>
        {
            // Version is unique per resource + scope (contract-specific or default)
            e.HasIndex(a => new { a.ResourceId, a.ContractId, a.Version }).IsUnique();
            e.HasIndex(a => new { a.ResourceId, a.EffectiveFrom });
            e.HasOne(a => a.Resource).WithMany(r => r.Agreements)
                .HasForeignKey(a => a.ResourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Contract).WithMany()
                .HasForeignKey(a => a.ContractId).OnDelete(DeleteBehavior.Restrict);
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
        });

        builder.Entity<Booking>(e =>
        {
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(b => b.PlannedHours).HasPrecision(9, 4);
            e.Property(b => b.ActivityRef).HasMaxLength(100);
            e.Property(b => b.EvidenceRef).HasMaxLength(200);
            e.Property(b => b.CustomerContact).HasMaxLength(200);
            e.HasIndex(b => new { b.ProjectId, b.Date });
            e.HasIndex(b => new { b.ResourceId, b.Date });
            e.HasOne(b => b.Project).WithMany()
                .HasForeignKey(b => b.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Resource).WithMany()
                .HasForeignKey(b => b.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Location).WithMany()
                .HasForeignKey(b => b.LocationId).OnDelete(DeleteBehavior.SetNull);
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
        });

        builder.Entity<AdditionalPadRequest>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Quantity).HasPrecision(9, 4);
            e.Property(r => r.Rate).HasPrecision(18, 2);
            e.Property(r => r.Reference).HasMaxLength(200);
            e.HasOne(r => r.Project).WithMany()
                .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProjectMonthClose>(e =>
        {
            e.HasIndex(c => new { c.ProjectId, c.Year, c.Month }).IsUnique();
            e.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
