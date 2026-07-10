using Ecert.DocsReview.Api.Contracts;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Application;

public enum ChangeStatusError
{
    None,
    NotFound,
    InvalidTransition,
    MissingRejectionReason,
}

/// <summary>
/// Outcome of a status-change attempt: either the updated document or the
/// reason it was refused. <see cref="FromStatus"/> carries the status the
/// document was in when a transition is refused, so the caller can report
/// it without a second query.
/// </summary>
public record ChangeStatusResult(
    DocumentResponse? Document, ChangeStatusError Error, DocumentStatus? FromStatus = null)
{
    public bool Succeeded => Error == ChangeStatusError.None && Document is not null;

    public static ChangeStatusResult Ok(DocumentResponse document) =>
        new(document, ChangeStatusError.None);

    public static ChangeStatusResult NotFound() => new(null, ChangeStatusError.NotFound);

    public static ChangeStatusResult InvalidTransition(DocumentStatus from) =>
        new(null, ChangeStatusError.InvalidTransition, from);

    public static ChangeStatusResult MissingReason() =>
        new(null, ChangeStatusError.MissingRejectionReason);
}
