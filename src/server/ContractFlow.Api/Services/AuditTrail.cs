using System.Globalization;
using System.Text.Json;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContractFlow.Api.Services;

/// <summary>Who is saving — resolved lazily per request so the DbContext never
/// depends on <see cref="ICurrentUser"/> directly (which itself needs the
/// DbContext).</summary>
public interface IAuditActor
{
    Task<AppUser?> ResolveAsync(CancellationToken ct);
}

/// <summary>Resolves the request's <see cref="ICurrentUser"/>; null outside a
/// request (seed, startup) — recorded as a system write.</summary>
public class HttpAuditActor(IHttpContextAccessor http) : IAuditActor
{
    public Task<AppUser?> ResolveAsync(CancellationToken ct)
    {
        var current = http.HttpContext?.RequestServices.GetService<ICurrentUser>();
        return current is null ? Task.FromResult<AppUser?>(null) : current.GetAsync(ct);
    }
}

/// <summary>
/// Builds <see cref="AuditEntry"/> rows from the change tracker (design §9.3).
/// Only commercially relevant entities are audited; bookings and timesheets
/// only when their status moves (other edits there are operational noise).
/// </summary>
public static class AuditTrail
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<Type> Audited =
    [
        typeof(Contract), typeof(ContractTerms), typeof(Customer),
        typeof(Project), typeof(WorkPackage), typeof(WorkPackageRate), typeof(Resource),
        typeof(CompensationAgreement), typeof(ProjectResource), typeof(ProjectAccess),
        typeof(Booking), typeof(Timesheet), typeof(InvoiceSpecification),
        typeof(SubcontractorStatement), typeof(SupplierInvoice), typeof(AdditionalPadRequest),
        typeof(ProjectMonthClose), typeof(EstimatedQuantity),
    ];

    /// <summary>Audited only when Status changes.</summary>
    private static readonly HashSet<Type> StatusOnly = [typeof(Booking), typeof(Timesheet)];

    /// <summary>Bookkeeping columns that carry no commercial meaning.</summary>
    private static readonly HashSet<string> Skipped = ["Id", "CreatedUtc", "UpdatedUtc"];

    /// <summary>Pending audit rows for the tracker's current changes. Call after
    /// DetectChanges and before the save so original values are still known.</summary>
    public static List<AuditEntry> Collect(ChangeTracker tracker)
    {
        var rows = new List<AuditEntry>();
        foreach (var entry in tracker.Entries())
        {
            var type = entry.Metadata.ClrType;
            if (!Audited.Contains(type) || entry.Metadata.IsOwned()) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            if (entry.Property("Id").CurrentValue is not Guid id) continue;

            var (action, changes) = entry.State switch
            {
                EntityState.Added => (AuditAction.Created, Values(entry, original: false)),
                EntityState.Deleted => (AuditAction.Deleted, Values(entry, original: true)),
                _ => (AuditAction.Updated, Modified(entry)),
            };
            if (action == AuditAction.Updated)
            {
                if (changes.Count == 0) continue;
                if (StatusOnly.Contains(type) && changes.All(c => c.Field != "Status")) continue;
            }

            rows.Add(new AuditEntry
            {
                EntityType = type.Name,
                EntityId = id,
                Action = action,
                Changes = JsonSerializer.Serialize(changes, Json),
            });
        }
        return rows;
    }

    public static List<AuditChange> Parse(string changes) =>
        JsonSerializer.Deserialize<List<AuditChange>>(changes, Json) ?? [];

    private static List<AuditChange> Values(EntityEntry entry, bool original) =>
        entry.Properties
            .Where(p => !Skipped.Contains(p.Metadata.Name))
            .Select(p => (p.Metadata.Name, Value: Format(original ? p.OriginalValue : p.CurrentValue)))
            .Where(x => x.Value is not null)
            .Select(x => original
                ? new AuditChange(x.Name, x.Value, null)
                : new AuditChange(x.Name, null, x.Value))
            .ToList();

    private static List<AuditChange> Modified(EntityEntry entry) =>
        entry.Properties
            .Where(p => p.IsModified && !Skipped.Contains(p.Metadata.Name))
            .Select(p => new AuditChange(p.Metadata.Name, Format(p.OriginalValue), Format(p.CurrentValue)))
            .Where(c => c.Old != c.New)
            .ToList();

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime d => d.ToString("O", Inv),
        DateOnly d => d.ToString("yyyy-MM-dd", Inv),
        Enum e => e.ToString(),
        // Normalised scale — 1000 and 1000.00 are the same value.
        decimal m => m.ToString("0.############", Inv),
        IFormattable f => f.ToString(null, Inv),
        _ => value.ToString(),
    };
}
