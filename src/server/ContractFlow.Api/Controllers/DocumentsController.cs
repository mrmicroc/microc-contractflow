using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Attachments (design §3 Document): expense receipts and booking evidence.
/// Upload is multipart; the bytes go to <see cref="IFileStorage"/> and the row
/// holds only metadata + the storage key.
/// </summary>
[ApiController]
[Route("api/documents")]
public class DocumentsController(AppDbContext db, ICurrentUser currentUser, IFileStorage storage)
    : ReportingControllerBase(db, currentUser)
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/webp", "text/plain",
    };

    public record DocumentDto(
        Guid Id, DocumentOwnerType OwnerType, Guid OwnerId, string FileName,
        string ContentType, long SizeBytes, string? Label, DateTime CreatedUtc);

    /// <summary>Lists attachments for one owner (e.g. an expense's receipts).</summary>
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource, AppRole.Finance)]
    public async Task<IActionResult> List(
        [FromQuery] DocumentOwnerType ownerType, [FromQuery] Guid ownerId, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ownerType, ownerId, ct);
        if (owner.Error is not null) return owner.Error;
        if (!await CanReadAsync(owner.ResourceId, ct)) return ForbidResult();

        var rows = await Db.Documents
            .Where(d => d.OwnerType == ownerType && d.OwnerId == ownerId)
            .OrderBy(d => d.CreatedUtc)
            .Select(d => new DocumentDto(d.Id, d.OwnerType, d.OwnerId, d.FileName,
                d.ContentType, d.SizeBytes, d.Label, d.CreatedUtc))
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Uploads one file (multipart field "file") onto an owner.</summary>
    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    [RequestSizeLimit(MaxBytes + 1024)]
    public async Task<IActionResult> Upload(
        [FromQuery] DocumentOwnerType ownerType, [FromQuery] Guid ownerId,
        [FromForm] IFormFile file, [FromForm] string? label, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ownerType, ownerId, ct);
        if (owner.Error is not null) return owner.Error;
        if (!await OwnsAsync(owner.ResourceId, ct)) return ForbidResult();
        if (owner.TimesheetLocked) return LockedError();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Empty file." });
        if (file.Length > MaxBytes)
            return BadRequest(new { error = $"File exceeds the {MaxBytes / 1024 / 1024} MB limit." });
        if (!AllowedContentTypes.Contains(file.ContentType ?? ""))
            return BadRequest(new { error = $"Content type '{file.ContentType}' is not allowed." });

        var me = await Me(ct);
        var key = await storage.SaveAsync(
            file.OpenReadStream(), Path.GetFileName(file.FileName), ct);
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerType = ownerType,
            OwnerId = ownerId,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            SizeBytes = file.Length,
            StorageKey = key,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            UploadedByUserId = me!.Id,
        };
        Db.Documents.Add(doc);
        await Db.SaveChangesAsync(ct);
        return Ok(new DocumentDto(doc.Id, doc.OwnerType, doc.OwnerId, doc.FileName,
            doc.ContentType, doc.SizeBytes, doc.Label, doc.CreatedUtc));
    }

    /// <summary>Streams the file back.</summary>
    [HttpGet("{id:guid}/content")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource, AppRole.Finance)]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var doc = await Db.Documents.FindAsync([id], ct);
        if (doc is null) return NotFound();
        var owner = await ResolveOwnerAsync(doc.OwnerType, doc.OwnerId, ct);
        if (owner.Error is not null) return owner.Error;
        if (!await CanReadAsync(owner.ResourceId, ct)) return ForbidResult();

        var stream = await storage.OpenReadAsync(doc.StorageKey, ct);
        return File(stream, doc.ContentType, doc.FileName);
    }

    /// <summary>Removes the attachment (row + stored bytes).</summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var doc = await Db.Documents.FindAsync([id], ct);
        if (doc is null) return NotFound();
        var owner = await ResolveOwnerAsync(doc.OwnerType, doc.OwnerId, ct);
        if (owner.Error is not null) return owner.Error;
        if (!await OwnsAsync(owner.ResourceId, ct)) return ForbidResult();
        if (owner.TimesheetLocked) return LockedError();

        Db.Documents.Remove(doc);
        await Db.SaveChangesAsync(ct);
        await storage.DeleteAsync(doc.StorageKey, ct);
        return NoContent();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Read = write roles + Finance (they process receipts).</summary>
    private async Task<bool> CanReadAsync(Guid resourceId, CancellationToken ct)
    {
        var me = await Me(ct);
        return me!.Role == AppRole.Finance || await OwnsAsync(resourceId, ct);
    }

    /// <summary>Maps an owner to the resource whose permissions apply, plus the
    /// timesheet lock that freezes expense receipts inside a submitted sheet.</summary>
    private async Task<(Guid ResourceId, bool TimesheetLocked, IActionResult? Error)>
        ResolveOwnerAsync(DocumentOwnerType ownerType, Guid ownerId, CancellationToken ct)
    {
        switch (ownerType)
        {
            case DocumentOwnerType.Expense:
            {
                var row = await Db.Expenses
                    .Where(e => e.Id == ownerId)
                    .Select(e => new { e.ResourceId, e.TimesheetId })
                    .FirstOrDefaultAsync(ct);
                if (row is null) return (Guid.Empty, false, NotFound());
                return (row.ResourceId, await LockedAsync(row.TimesheetId, ct), null);
            }
            case DocumentOwnerType.Booking:
            {
                var resourceId = await Db.Bookings
                    .Where(b => b.Id == ownerId)
                    .Select(b => (Guid?)b.ResourceId)
                    .FirstOrDefaultAsync(ct);
                if (resourceId is null) return (Guid.Empty, false, NotFound());
                return (resourceId.Value, false, null);
            }
            default:
                return (Guid.Empty, false, BadRequest(new { error = "Unknown owner type." }));
        }
    }
}
