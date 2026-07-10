namespace Ecert.DocsReview.Api.Infrastructure.Storage;

/// <summary>
/// Abstraction over where PDF bytes live. Keeps the physical store (local
/// disk today, object storage later) out of the application logic.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Persists <paramref name="content"/> at the given relative path and
    /// returns the number of bytes written. Overwrites if it already exists.
    /// </summary>
    Task<long> SaveAsync(string relativePath, Stream content, CancellationToken ct = default);

    /// <summary>Opens a stored file for reading.</summary>
    Stream OpenRead(string relativePath);

    /// <summary>Builds the relative path for a document version: {documentId}/v{n}.pdf.</summary>
    string BuildVersionPath(Guid documentId, int versionNumber);
}
