using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>Full document detail, including its version history.</summary>
public record DocumentResponse(
    Guid Id,
    string Title,
    DocumentType Type,
    DocumentStatus Status,
    int? CurrentVersionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DocumentVersionResponse> Versions)
{
    public static DocumentResponse From(Document document) => new(
        document.Id,
        document.Title,
        document.Type,
        document.Status,
        document.CurrentVersion?.VersionNumber,
        document.CreatedAt,
        document.UpdatedAt,
        document.Versions
            .OrderBy(v => v.VersionNumber)
            .Select(DocumentVersionResponse.From)
            .ToList());
}
