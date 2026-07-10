using System.ComponentModel.DataAnnotations;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>
/// Multipart form for registering a new document together with its first
/// PDF version. Bound from <c>multipart/form-data</c> so the metadata and
/// the file arrive in a single request.
/// </summary>
public class RegisterDocumentRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public DocumentType Type { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string UploadedBy { get; set; } = string.Empty;

    [Required]
    public IFormFile File { get; set; } = default!;
}
