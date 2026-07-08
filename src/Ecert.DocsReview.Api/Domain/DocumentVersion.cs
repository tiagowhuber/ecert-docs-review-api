namespace Ecert.DocsReview.Api.Domain;

public class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>Assigned server-side (max + 1), unique per document.</summary>
    public int VersionNumber { get; set; }

    public required string FileName { get; set; }

    /// <summary>Path relative to the storage root: {documentId}/v{versionNumber}.pdf</summary>
    public required string StoragePath { get; set; }

    public long FileSizeBytes { get; set; }
    public required string Sha256 { get; set; }

    /// <summary>Filled by the PDF analysis service (PdfPig); null until analyzed.</summary>
    public int? PageCount { get; set; }

    public required string UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    public List<Observation> Observations { get; set; } = [];
}
