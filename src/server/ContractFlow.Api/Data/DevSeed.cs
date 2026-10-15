using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Data;

/// <summary>
/// Neutral development users — one per role — so the local dev role-switcher
/// has someone to impersonate. Real users are created through Entra sign-in later.
/// </summary>
public static class DevSeed
{
    public static readonly Guid AdminId    = Guid.Parse("00000000-0000-4000-8000-000000000001");
    public static readonly Guid ManagerId  = Guid.Parse("00000000-0000-4000-8000-000000000002");
    public static readonly Guid ResourceId = Guid.Parse("00000000-0000-4000-8000-000000000003");
    public static readonly Guid FinanceId  = Guid.Parse("00000000-0000-4000-8000-000000000004");

    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        db.Users.AddRange(
            new AppUser { Id = AdminId,    DisplayName = "Dev Admin",      Role = AppRole.Administrator },
            new AppUser { Id = ManagerId,  DisplayName = "Dev Manager",    Role = AppRole.ProjectManager },
            new AppUser { Id = ResourceId, DisplayName = "Dev Consultant", Role = AppRole.Resource },
            new AppUser { Id = FinanceId,  DisplayName = "Dev Finance",    Role = AppRole.Finance });

        await db.SaveChangesAsync();
    }
}
