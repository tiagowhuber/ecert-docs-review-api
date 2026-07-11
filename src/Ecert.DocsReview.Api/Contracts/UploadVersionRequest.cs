using System.ComponentModel.DataAnnotations;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>
/// Multipart form for uploading a new version of an existing document.
/// Bound from <c>multipart/form-data</c>.
/// </summary>
public class UploadVersionRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string UploadedBy { get; set; } = string.Empty;

    [Required]
    public IFormFile File { get; set; } = default!;
}
