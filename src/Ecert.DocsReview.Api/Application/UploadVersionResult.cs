using Ecert.DocsReview.Api.Contracts;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Application;

public enum UploadVersionError
{
    None,
    NotFound,
    VersionNotAllowed,
    EmptyFile,
    FileTooLarge,
    NotAPdf,
    DuplicateContent,
}

/// <summary>
/// Outcome of a version-upload attempt: either the updated document or the
/// reason it was refused. <see cref="CurrentStatus"/> carries the status the
/// document was in when the upload is not allowed, so the caller can report
/// it without a second query.
/// </summary>
public record UploadVersionResult(
    DocumentResponse? Document, UploadVersionError Error, DocumentStatus? CurrentStatus = null)
{
    public bool Succeeded => Error == UploadVersionError.None && Document is not null;

    public static UploadVersionResult Ok(DocumentResponse document) =>
        new(document, UploadVersionError.None);

    public static UploadVersionResult NotFound() => new(null, UploadVersionError.NotFound);

    public static UploadVersionResult NotAllowed(DocumentStatus current) =>
        new(null, UploadVersionError.VersionNotAllowed, current);

    public static UploadVersionResult Fail(UploadVersionError error) => new(null, error);
}
