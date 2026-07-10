using Microsoft.Extensions.Options;

namespace Ecert.DocsReview.Api.Infrastructure.Storage;

/// <summary>Stores files on the local filesystem under a configured root.</summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        var root = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Missing Storage:RootPath configuration.");
        }
        _rootPath = Path.GetFullPath(root);
    }

    public async Task<long> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var fullPath = ResolveFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var target = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(target, ct);
        return target.Length;
    }

    public Stream OpenRead(string relativePath) =>
        new FileStream(ResolveFullPath(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);

    public string BuildVersionPath(Guid documentId, int versionNumber) =>
        Path.Combine(documentId.ToString(), $"v{versionNumber}.pdf");

    private string ResolveFullPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        // Guard against path traversal escaping the storage root.
        if (!fullPath.StartsWith(_rootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolved path '{fullPath}' escapes the storage root.");
        }
        return fullPath;
    }
}
