using Ecert.DocsReview.Api.Contracts;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Application;

public enum AddObservationError
{
    None,
    NotFound,
    NotAllowed,
    RejectionReasonNotAllowed,
}

/// <summary>
/// Outcome of an add-observation attempt: either the created observation or
/// the reason it was refused. <see cref="CurrentStatus"/> carries the status
/// the document was in when the observation is refused, so the caller can
/// report it without a second query.
/// </summary>
public record AddObservationResult(
    ObservationResponse? Observation, AddObservationError Error, DocumentStatus? CurrentStatus = null)
{
    public bool Succeeded => Error == AddObservationError.None && Observation is not null;

    public static AddObservationResult Ok(ObservationResponse observation) =>
        new(observation, AddObservationError.None);

    public static AddObservationResult NotFound() => new(null, AddObservationError.NotFound);

    public static AddObservationResult NotAllowed(DocumentStatus status) =>
        new(null, AddObservationError.NotAllowed, status);

    public static AddObservationResult RejectionReasonNotAllowed() =>
        new(null, AddObservationError.RejectionReasonNotAllowed);
}
