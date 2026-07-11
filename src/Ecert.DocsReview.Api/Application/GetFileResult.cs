namespace Ecert.DocsReview.Api.Application;

public enum GetFileError
{
    None,
    DocumentNotFound,
    VersionNotFound,
}

/// <summary>Outcome of a file-download request: the stored PDF stream or why it was refused.</summary>
public record GetFileResult(Stream? Content, string? FileName, GetFileError Error)
{
    public bool Succeeded => Error == GetFileError.None && Content is not null;

    public static GetFileResult Ok(Stream content, string fileName) =>
        new(content, fileName, GetFileError.None);

    public static GetFileResult DocumentNotFound() =>
        new(null, null, GetFileError.DocumentNotFound);

    public static GetFileResult VersionNotFound() =>
        new(null, null, GetFileError.VersionNotFound);
}
