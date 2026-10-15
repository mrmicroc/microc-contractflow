using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Data;

/// <summary>
/// Neutral development data. <see cref="SeedIfEmptyAsync"/> seeds one user per
/// role so the local dev role-switcher has someone to impersonate — it also
/// runs in tests. <see cref="SeedCommercialExampleAsync"/> seeds a neutral
/// customer→contract→project→resource graph for clicking around in dev only;
/// tests get clean commercial tables. All names are fictional examples — real
/// customers and contract values are entered by admins, never seeded
/// (see AGENTS.md).
/// </summary>
public static class DevSeed
{
    public static readonly Guid AdminId    = Guid.Parse("00000000-0000-4000-8000-000000000001");
    public static readonly Guid ManagerId  = Guid.Parse("00000000-0000-4000-8000-000000000002");
    public static readonly Guid ResourceId = Guid.Parse("00000000-0000-4000-8000-000000000003");
    public static readonly Guid FinanceId  = Guid.Parse("00000000-0000-4000-8000-000000000004");
    public static readonly Guid ReviewerId = Guid.Parse("00000000-0000-4000-8000-000000000005");

    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        db.Users.AddRange(
            new AppUser { Id = AdminId,    DisplayName = "Dev Admin",      Role = AppRole.Administrator },
            new AppUser { Id = ManagerId,  DisplayName = "Dev Manager",    Role = AppRole.ProjectManager },
            new AppUser { Id = ResourceId, DisplayName = "Dev Consultant", Role = AppRole.Resource },
            new AppUser { Id = FinanceId,  DisplayName = "Dev Finance",    Role = AppRole.Finance },
            new AppUser { Id = ReviewerId, DisplayName = "Dev Reviewer",   Role = AppRole.CustomerReviewer });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Neutral example commercial structure for local development — not run by
    /// tests. Values are obviously fictional placeholders, not real rates.
    /// </summary>
    public static async Task SeedCommercialExampleAsync(AppDbContext db)
    {
        if (await db.Customers.AnyAsync()) return;

        var customer = new Customer { Name = "Example Org", ExternalReference = "EX-100" };
        var endClient = new EndClient { Name = "North Link" };

        var contract = new Contract
        {
            Customer = customer,
            EndClient = endClient,
            Name = "Framework Inspection Services",
            ExternalReference = "FW-2026-01",
            Model = ContractModel.PadBased,
            StartDate = new DateOnly(2026, 1, 1),
        };
        contract.TermsVersions.Add(new ContractTerms
        {
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Currency = "SEK",
            PadRate = 100m,
            HourlyRate = 10m,
            SaturdayRate = 12m,
            SundayRate = 14m,
            MobilisationFee = 40m,
            ExpenseMarkupPercent = 10m,
            HolidayCalendar = "SE",
        });

        var project = new Project
        {
            Contract = contract,
            Name = "Substation Inspections North",
            Code = "SN-01",
            Description = "Recurring substation inspections, northern region.",
        };
        project.Locations.Add(new Location
        {
            Name = "Substation Site A",
            Address = "1 Example Road",
        });
        project.EstimatedQuantities.Add(new EstimatedQuantity
        {
            Label = "Inspection days 2026",
            Quantity = 120m,
            Unit = "days",
        });

        var consultant = new Resource
        {
            DisplayName = "Dev Consultant",
            Type = ResourceType.Subcontractor,
            Email = "consultant@example.invalid",
            Competencies = "Example certification A",
            UserId = ResourceId,
        };
        consultant.Agreements.Add(new CompensationAgreement
        {
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Currency = "SEK",
            HourlyRate = 10m,
            TravelTimePercent = 50m,
            MileageRate = 2m,
            PerDiem = PerDiemPolicy.Custom,
            PerDiemAmount = 5m,
        });

        var employee = new Resource
        {
            DisplayName = "Casey Example",
            Type = ResourceType.Employee,
            Competencies = "Example certification B",
        };
        employee.Leave.Add(new ResourceLeave
        {
            From = new DateOnly(2026, 7, 6),
            To = new DateOnly(2026, 7, 31),
            Type = LeaveType.Vacation,
        });

        db.Customers.Add(customer);
        db.EndClients.Add(endClient);
        db.Contracts.Add(contract);
        db.Projects.Add(project);
        db.Resources.AddRange(consultant, employee);

        await db.SaveChangesAsync();
    }
}
