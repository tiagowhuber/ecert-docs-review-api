using System.ComponentModel.DataAnnotations;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Api.Contracts;

/// <summary>Request to move a document to a new lifecycle status.</summary>
public class ChangeDocumentStatusRequest
{
    // Nullable so a missing field fails [Required] instead of silently
    // binding to the enum default (Created).
    [Required]
    public DocumentStatus? TargetStatus { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string PerformedBy { get; set; } = string.Empty;

    /// <summary>Required when rejecting; stored as a RejectionReason observation.</summary>
    [StringLength(1000)]
    public string? Reason { get; set; }

    /// <summary>Optional free-form note recorded on the audit event.</summary>
    [StringLength(1000)]
    public string? Details { get; set; }
}
