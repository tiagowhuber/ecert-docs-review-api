using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>One entry in a document's audit trail.</summary>
public record DocumentEventResponse(
    long Id,
    DocumentEventType EventType,
    DocumentStatus? FromStatus,
    DocumentStatus? ToStatus,
    string? Details,
    string PerformedBy,
    DateTimeOffset OccurredAt)
{
    public static DocumentEventResponse From(DocumentEvent documentEvent) => new(
        documentEvent.Id,
        documentEvent.EventType,
        documentEvent.FromStatus,
        documentEvent.ToStatus,
        documentEvent.Details,
        documentEvent.PerformedBy,
        documentEvent.OccurredAt);
}
