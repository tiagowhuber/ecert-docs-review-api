using System.ComponentModel.DataAnnotations;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>Request to record an observation on a document's current version.</summary>
public class AddObservationRequest
{
    // Nullable so a missing field fails [Required] instead of silently
    // binding to the enum default (Comment).
    [Required]
    public ObservationType? Type { get; set; }

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CreatedBy { get; set; } = string.Empty;
}
