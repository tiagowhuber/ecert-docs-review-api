namespace Ecert.DocsReview.Api.Domain;

public class Document
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public DocumentType Type { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Created;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<DocumentVersion> Versions { get; set; } = [];
    public List<DocumentEvent> Events { get; set; } = [];

    /// <summary>
    /// The current version is always the highest version number — computed,
    /// never stored, so it cannot drift out of sync with the version history.
    /// </summary>
    public DocumentVersion? CurrentVersion =>
        Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
}
