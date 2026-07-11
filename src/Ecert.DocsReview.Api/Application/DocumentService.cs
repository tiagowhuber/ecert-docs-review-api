using System.Security.Cryptography;
using Ecert.DocsReview.Api.Contracts;
using Ecert.DocsReview.Api.Domain;
using Ecert.DocsReview.Api.Infrastructure;
using Ecert.DocsReview.Api.Infrastructure.Pdf;
using Ecert.DocsReview.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Ecert.DocsReview.Api.Application;

/// <summary>
/// Orchestrates document registration and consultation: validates the PDF,
/// persists the file plus entities, and writes the audit trail in one
/// transaction so the log can never disagree with the data.
/// </summary>
public class DocumentService
{
    /// <summary>Upper bound on an uploaded PDF; keeps a stray large file from exhausting memory/disk.</summary>
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IPdfAnalyzer _analyzer;

    public DocumentService(AppDbContext db, IFileStorage storage, IPdfAnalyzer analyzer)
    {
        _db = db;
        _storage = storage;
        _analyzer = analyzer;
    }

    public async Task<RegisterDocumentResult> RegisterAsync(
        RegisterDocumentRequest request, CancellationToken ct = default)
    {
        var (bytes, error) = await ReadValidPdfAsync(request.File, ct);
        if (bytes is null)
        {
            return RegisterDocumentResult.Fail(error);
        }

        var now = DateTimeOffset.UtcNow;
        var documentId = Guid.NewGuid();
        var storagePath = _storage.BuildVersionPath(documentId, 1);

        using var buffer = new MemoryStream(bytes);
        var sizeBytes = await _storage.SaveAsync(storagePath, buffer, ct);

        buffer.Position = 0;
        var analysis = await _analyzer.AnalyzeAsync(buffer, ct);

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = 1,
            FileName = SafeFileName(request.File.FileName),
            StoragePath = storagePath,
            FileSizeBytes = sizeBytes,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            PageCount = analysis.PageCount,
            UploadedBy = request.UploadedBy,
            UploadedAt = now,
        };

        var document = new Document
        {
            Id = documentId,
            Title = request.Title,
            Type = request.Type,
            Status = DocumentStatus.Created,
            CreatedAt = now,
            UpdatedAt = now,
            Versions = { version },
            Events =
            {
                new DocumentEvent
                {
                    EventType = DocumentEventType.DocumentCreated,
                    Details = $"Document '{request.Title}' registered.",
                    PerformedBy = request.UploadedBy,
                    OccurredAt = now,
                },
                new DocumentEvent
                {
                    EventType = DocumentEventType.VersionUploaded,
                    Details = $"Version {version.VersionNumber} uploaded ({version.FileName}).",
                    PerformedBy = request.UploadedBy,
                    OccurredAt = now,
                },
            },
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        return RegisterDocumentResult.Ok(DocumentResponse.From(document));
    }

    public async Task<UploadVersionResult> UploadVersionAsync(
        Guid id, UploadVersionRequest request, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Versions)
            .SingleOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return UploadVersionResult.NotFound();
        }

        // State check before file validation: whether an upload is allowed
        // must not depend on the payload.
        if (!DocumentStateMachine.CanUploadVersion(document.Status))
        {
            return UploadVersionResult.NotAllowed(document.Status);
        }

        var (bytes, fileError) = await ReadValidPdfAsync(request.File, ct);
        if (bytes is null)
        {
            return UploadVersionResult.Fail(fileError switch
            {
                RegisterError.EmptyFile => UploadVersionError.EmptyFile,
                RegisterError.FileTooLarge => UploadVersionError.FileTooLarge,
                _ => UploadVersionError.NotAPdf,
            });
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (sha256 == document.CurrentVersion!.Sha256)
        {
            return UploadVersionResult.Fail(UploadVersionError.DuplicateContent);
        }

        var now = DateTimeOffset.UtcNow;
        var versionNumber = document.Versions.Max(v => v.VersionNumber) + 1;
        var storagePath = _storage.BuildVersionPath(document.Id, versionNumber);

        using var buffer = new MemoryStream(bytes);
        var sizeBytes = await _storage.SaveAsync(storagePath, buffer, ct);

        buffer.Position = 0;
        var analysis = await _analyzer.AnalyzeAsync(buffer, ct);

        var fileName = SafeFileName(request.File.FileName);
        // Ids left unset: setting a key on an entity attached to a tracked
        // graph makes EF treat it as an update to an existing row.
        document.Versions.Add(new DocumentVersion
        {
            VersionNumber = versionNumber,
            FileName = fileName,
            StoragePath = storagePath,
            FileSizeBytes = sizeBytes,
            Sha256 = sha256,
            PageCount = analysis.PageCount,
            UploadedBy = request.UploadedBy,
            UploadedAt = now,
        });
        document.Events.Add(new DocumentEvent
        {
            EventType = DocumentEventType.VersionUploaded,
            Details = $"Version {versionNumber} uploaded ({fileName}).",
            PerformedBy = request.UploadedBy,
            OccurredAt = now,
        });

        var newStatus = DocumentStateMachine.StatusAfterVersionUpload(document.Status);
        if (newStatus != document.Status)
        {
            document.Events.Add(new DocumentEvent
            {
                EventType = DocumentEventType.StatusChanged,
                FromStatus = document.Status,
                ToStatus = newStatus,
                Details = "New version uploaded; document re-entered the review queue.",
                PerformedBy = request.UploadedBy,
                OccurredAt = now,
            });
            document.Status = newStatus;
        }

        document.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return UploadVersionResult.Ok(DocumentResponse.From(document));
    }

    /// <summary>Opens the stored PDF for a version; null <paramref name="versionNumber"/> means the current version.</summary>
    public async Task<GetFileResult> GetVersionFileAsync(
        Guid id, int? versionNumber, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .Include(d => d.Versions)
            .SingleOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return GetFileResult.DocumentNotFound();
        }

        var version = versionNumber is null
            ? document.CurrentVersion
            : document.Versions.SingleOrDefault(v => v.VersionNumber == versionNumber);
        if (version is null)
        {
            return GetFileResult.VersionNotFound();
        }

        return GetFileResult.Ok(_storage.OpenRead(version.StoragePath), version.FileName);
    }

    public async Task<ChangeStatusResult> ChangeStatusAsync(
        Guid id, ChangeDocumentStatusRequest request, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Versions)
            .SingleOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return ChangeStatusResult.NotFound();
        }

        var target = request.TargetStatus!.Value;
        if (!DocumentStateMachine.CanTransition(document.Status, target))
        {
            return ChangeStatusResult.InvalidTransition(document.Status);
        }
        if (target == DocumentStatus.Rejected && string.IsNullOrWhiteSpace(request.Reason))
        {
            return ChangeStatusResult.MissingReason();
        }

        var now = DateTimeOffset.UtcNow;

        if (target == DocumentStatus.Rejected)
        {
            var version = document.CurrentVersion!;
            // Id left unset: setting a key on an entity attached to a tracked
            // graph makes EF treat it as an update to an existing row.
            version.Observations.Add(new Observation
            {
                Type = ObservationType.RejectionReason,
                Content = request.Reason!,
                CreatedBy = request.PerformedBy,
                CreatedAt = now,
            });
            document.Events.Add(new DocumentEvent
            {
                EventType = DocumentEventType.ObservationAdded,
                Details = $"Rejection reason recorded on version {version.VersionNumber}.",
                PerformedBy = request.PerformedBy,
                OccurredAt = now,
            });
        }

        var from = document.Status;
        document.Status = target;
        document.UpdatedAt = now;
        document.Events.Add(new DocumentEvent
        {
            EventType = DocumentEventType.StatusChanged,
            FromStatus = from,
            ToStatus = target,
            Details = request.Details ?? $"Status changed from {from} to {target}.",
            PerformedBy = request.PerformedBy,
            OccurredAt = now,
        });

        await _db.SaveChangesAsync(ct);

        return ChangeStatusResult.Ok(DocumentResponse.From(document));
    }

    public async Task<DocumentResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .Include(d => d.Versions)
            .SingleOrDefaultAsync(d => d.Id == id, ct);

        return document is null ? null : DocumentResponse.From(document);
    }

    public async Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(
        DocumentStatus? status, DocumentType? type, CancellationToken ct = default)
    {
        var query = _db.Documents
            .AsNoTracking()
            .Include(d => d.Versions)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(d => d.Status == status);
        }
        if (type is not null)
        {
            query = query.Where(d => d.Type == type);
        }

        // Order in memory: SQLite (used by the integration tests) can't ORDER BY
        // a DateTimeOffset. The filtered set is small, so this is inexpensive.
        var documents = await query.ToListAsync(ct);

        return documents
            .OrderByDescending(d => d.CreatedAt)
            .Select(DocumentSummaryResponse.From)
            .ToList();
    }

    /// <summary>
    /// Buffers the upload once and runs the shared PDF checks (empty, size,
    /// %PDF- magic), so callers can validate, hash, analyze, and store from
    /// the same bytes without re-reading the (forward-only) upload stream.
    /// Bytes is null exactly when Error is not None.
    /// </summary>
    private static async Task<(byte[]? Bytes, RegisterError Error)> ReadValidPdfAsync(
        IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return (null, RegisterError.EmptyFile);
        }
        if (file.Length > MaxFileSizeBytes)
        {
            return (null, RegisterError.FileTooLarge);
        }

        using var buffer = new MemoryStream();
        await using (var upload = file.OpenReadStream())
        {
            await upload.CopyToAsync(buffer, ct);
        }
        var bytes = buffer.ToArray();

        return HasPdfMagic(bytes) ? (bytes, RegisterError.None) : (null, RegisterError.NotAPdf);
    }

    private static bool HasPdfMagic(byte[] bytes) =>
        bytes.Length >= PdfMagic.Length && bytes.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic);

    private static string SafeFileName(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? "document.pdf" : Path.GetFileName(fileName);
}
