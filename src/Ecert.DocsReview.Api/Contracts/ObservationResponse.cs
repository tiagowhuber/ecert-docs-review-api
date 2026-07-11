using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>An observation recorded on a specific version of a document.</summary>
public record ObservationResponse(
    Guid Id,
    int VersionNumber,
    ObservationType Type,
    string Content,
    string CreatedBy,
    DateTimeOffset CreatedAt)
{
    public static ObservationResponse From(Observation observation, int versionNumber) => new(
        observation.Id,
        versionNumber,
        observation.Type,
        observation.Content,
        observation.CreatedBy,
        observation.CreatedAt);
}
