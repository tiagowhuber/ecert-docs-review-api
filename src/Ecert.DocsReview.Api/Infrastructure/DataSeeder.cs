using System.Security.Cryptography;
using System.Text;
using Ecert.DocsReview.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ecert.DocsReview.Api.Infrastructure;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, string storageRoot, CancellationToken ct = default)
    {
        if (await db.Documents.AnyAsync(ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Document 1: freshly submitted, waiting for a reviewer.
        var contract = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Service Contract 2026",
            Type = DocumentType.Contract,
            Status = DocumentStatus.PendingReview,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-2),
        };
        var contractV1 = AddVersion(contract, 1, "service-contract-2026.pdf", "juan.author", now.AddDays(-2), storageRoot);
        contract.Events.AddRange(
        [
            CreatedEvent(contract, "juan.author", now.AddDays(-2)),
            VersionEvent(contract, contractV1, now.AddDays(-2)),
            StatusEvent(contract, DocumentStatus.Created, DocumentStatus.PendingReview, "juan.author", now.AddDays(-2)),
        ]);

        // Document 2: went through two review rounds, currently rejected.
        var report = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Quarterly Report Q1",
            Type = DocumentType.Report,
            Status = DocumentStatus.Rejected,
            CreatedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-1),
        };
        var reportV1 = AddVersion(report, 1, "quarterly-report-q1.pdf", "ana.author", now.AddDays(-10), storageRoot);
        var reportV2 = AddVersion(report, 2, "quarterly-report-q1-fixed.pdf", "ana.author", now.AddDays(-4), storageRoot);
        reportV1.Observations.Add(new Observation
        {
            Id = Guid.NewGuid(),
            Type = ObservationType.RejectionReason,
            Content = "Revenue figures in section 2 do not match the annex totals.",
            CreatedBy = "maria.reviewer",
            CreatedAt = now.AddDays(-6),
        });
        reportV2.Observations.Add(new Observation
        {
            Id = Guid.NewGuid(),
            Type = ObservationType.RejectionReason,
            Content = "Section 2 corrected, but the executive summary is still outdated.",
            CreatedBy = "maria.reviewer",
            CreatedAt = now.AddDays(-1),
        });
        report.Events.AddRange(
        [
            CreatedEvent(report, "ana.author", now.AddDays(-10)),
            VersionEvent(report, reportV1, now.AddDays(-10)),
            StatusEvent(report, DocumentStatus.Created, DocumentStatus.PendingReview, "ana.author", now.AddDays(-10)),
            StatusEvent(report, DocumentStatus.PendingReview, DocumentStatus.UnderReview, "maria.reviewer", now.AddDays(-7)),
            ObservationEvent(report, "Rejection reason recorded on version 1.", "maria.reviewer", now.AddDays(-6)),
            StatusEvent(report, DocumentStatus.UnderReview, DocumentStatus.Rejected, "maria.reviewer", now.AddDays(-6)),
            VersionEvent(report, reportV2, now.AddDays(-4)),
            StatusEvent(report, DocumentStatus.Rejected, DocumentStatus.PendingReview, "ana.author", now.AddDays(-4)),
            StatusEvent(report, DocumentStatus.PendingReview, DocumentStatus.UnderReview, "maria.reviewer", now.AddDays(-2)),
            ObservationEvent(report, "Rejection reason recorded on version 2.", "maria.reviewer", now.AddDays(-1)),
            StatusEvent(report, DocumentStatus.UnderReview, DocumentStatus.Rejected, "maria.reviewer", now.AddDays(-1)),
        ]);

        // Document 3: reviewed and approved.
        var quotation = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Pricing Quotation - Cert Renewal",
            Type = DocumentType.Quotation,
            Status = DocumentStatus.Approved,
            CreatedAt = now.AddDays(-5),
            UpdatedAt = now.AddDays(-3),
        };
        var quotationV1 = AddVersion(quotation, 1, "pricing-quotation.pdf", "juan.author", now.AddDays(-5), storageRoot);
        quotationV1.Observations.Add(new Observation
        {
            Id = Guid.NewGuid(),
            Type = ObservationType.Comment,
            Content = "Prices verified against the current rate card.",
            CreatedBy = "maria.reviewer",
            CreatedAt = now.AddDays(-3),
        });
        quotation.Events.AddRange(
        [
            CreatedEvent(quotation, "juan.author", now.AddDays(-5)),
            VersionEvent(quotation, quotationV1, now.AddDays(-5)),
            StatusEvent(quotation, DocumentStatus.Created, DocumentStatus.PendingReview, "juan.author", now.AddDays(-5)),
            StatusEvent(quotation, DocumentStatus.PendingReview, DocumentStatus.UnderReview, "maria.reviewer", now.AddDays(-4)),
            ObservationEvent(quotation, "Comment recorded on version 1.", "maria.reviewer", now.AddDays(-3)),
            StatusEvent(quotation, DocumentStatus.UnderReview, DocumentStatus.Approved, "maria.reviewer", now.AddDays(-3)),
        ]);

        db.Documents.AddRange(contract, report, quotation);
        await db.SaveChangesAsync(ct);
    }

    private static DocumentVersion AddVersion(
        Document document, int versionNumber, string fileName, string uploadedBy,
        DateTimeOffset uploadedAt, string storageRoot)
    {
        var storagePath = Path.Combine(document.Id.ToString(), $"v{versionNumber}.pdf");
        var bytes = CreateMinimalPdf($"{document.Title} - version {versionNumber}");

        var fullPath = Path.Combine(storageRoot, storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = versionNumber,
            FileName = fileName,
            StoragePath = storagePath,
            FileSizeBytes = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            PageCount = 1,
            UploadedBy = uploadedBy,
            UploadedAt = uploadedAt,
        };
        document.Versions.Add(version);
        return version;
    }

    /// <summary>
    /// Builds a tiny but structurally valid single-page PDF (correct xref
    /// offsets), so seeded files can be opened by any PDF reader/analyzer.
    /// Text must be plain ASCII without parentheses or backslashes.
    /// </summary>
    private static byte[] CreateMinimalPdf(string text)
    {
        var content = $"BT /F1 18 Tf 72 720 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = sb.Length;
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append($"{offset:D10} 00000 n \n");
        }
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static DocumentEvent CreatedEvent(Document document, string by, DateTimeOffset at) => new()
    {
        EventType = DocumentEventType.DocumentCreated,
        Details = $"Document '{document.Title}' registered.",
        PerformedBy = by,
        OccurredAt = at,
    };

    private static DocumentEvent VersionEvent(Document document, DocumentVersion version, DateTimeOffset at) => new()
    {
        EventType = DocumentEventType.VersionUploaded,
        Details = $"Version {version.VersionNumber} uploaded ({version.FileName}).",
        PerformedBy = version.UploadedBy,
        OccurredAt = at,
    };

    private static DocumentEvent StatusEvent(
        Document document, DocumentStatus from, DocumentStatus to, string by, DateTimeOffset at) => new()
    {
        EventType = DocumentEventType.StatusChanged,
        FromStatus = from,
        ToStatus = to,
        Details = $"Status changed from {from} to {to}.",
        PerformedBy = by,
        OccurredAt = at,
    };

    private static DocumentEvent ObservationEvent(Document document, string details, string by, DateTimeOffset at) => new()
    {
        EventType = DocumentEventType.ObservationAdded,
        Details = details,
        PerformedBy = by,
        OccurredAt = at,
    };
}
