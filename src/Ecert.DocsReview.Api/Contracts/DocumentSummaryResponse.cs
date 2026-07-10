using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>Lightweight document row for list endpoints.</summary>
public record DocumentSummaryResponse(
    Guid Id,
    string Title,
    DocumentType Type,
    DocumentStatus Status,
    int? CurrentVersionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static DocumentSummaryResponse From(Document document) => new(
        document.Id,
        document.Title,
        document.Type,
        document.Status,
        document.CurrentVersion?.VersionNumber,
        document.CreatedAt,
        document.UpdatedAt);
}
