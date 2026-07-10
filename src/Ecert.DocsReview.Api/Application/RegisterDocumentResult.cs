using Ecert.DocsReview.Api.Contracts;

namespace Ecert.DocsReview.Api.Application;

public enum RegisterError
{
    None,
    EmptyFile,
    FileTooLarge,
    NotAPdf,
}

/// <summary>Outcome of a registration attempt: either the created document or a validation error.</summary>
public record RegisterDocumentResult(DocumentResponse? Document, RegisterError Error)
{
    public bool Succeeded => Error == RegisterError.None && Document is not null;

    public static RegisterDocumentResult Ok(DocumentResponse document) => new(document, RegisterError.None);

    public static RegisterDocumentResult Fail(RegisterError error) => new(null, error);
}
