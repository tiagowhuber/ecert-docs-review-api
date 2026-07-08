namespace Ecert.DocsReview.Api.Domain;

/// <summary>
/// Append-only audit log. Written in the same transaction as the change it
/// records, so the trail can never disagree with the data.
/// </summary>
public class DocumentEvent
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public DocumentEventType EventType { get; set; }
    public DocumentStatus? FromStatus { get; set; }
    public DocumentStatus? ToStatus { get; set; }
    public string? Details { get; set; }
    public required string PerformedBy { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
