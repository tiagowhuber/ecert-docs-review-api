using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>A single version of a document.</summary>
public record DocumentVersionResponse(
    Guid Id,
    int VersionNumber,
    string FileName,
    long FileSizeBytes,
    string Sha256,
    int? PageCount,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    IReadOnlyList<ObservationResponse> Observations)
{
    public static DocumentVersionResponse From(DocumentVersion version) => new(
        version.Id,
        version.VersionNumber,
        version.FileName,
        version.FileSizeBytes,
        version.Sha256,
        version.PageCount,
        version.UploadedBy,
        version.UploadedAt,
        version.Observations
            .OrderBy(o => o.CreatedAt)
            .Select(o => ObservationResponse.From(o, version.VersionNumber))
            .ToList());
}
