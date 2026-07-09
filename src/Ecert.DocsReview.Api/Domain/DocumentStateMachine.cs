namespace Ecert.DocsReview.Api.Domain;

/// <summary>
/// Pure domain rules for the document lifecycle. No EF/HTTP dependencies,
/// so every rule is unit-testable in isolation.
/// </summary>
public static class DocumentStateMachine
{
    private static readonly IReadOnlyDictionary<DocumentStatus, DocumentStatus[]> ValidTransitions =
        new Dictionary<DocumentStatus, DocumentStatus[]>
        {
            [DocumentStatus.Created] = [DocumentStatus.PendingReview],
            [DocumentStatus.PendingReview] = [DocumentStatus.UnderReview],
            [DocumentStatus.UnderReview] = [DocumentStatus.Approved, DocumentStatus.Rejected],
            [DocumentStatus.Approved] = [DocumentStatus.Archived],
            [DocumentStatus.Rejected] = [DocumentStatus.PendingReview, DocumentStatus.Archived],
            [DocumentStatus.Archived] = [],
        };

    public static bool CanTransition(DocumentStatus from, DocumentStatus to) =>
        ValidTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>New versions are only accepted while the document is editable.</summary>
    public static bool CanUploadVersion(DocumentStatus status) =>
        status is DocumentStatus.Created or DocumentStatus.PendingReview or DocumentStatus.Rejected;

    /// <summary>
    /// Status the document ends up in after a successful version upload:
    /// a rejected document re-enters the review queue.
    /// </summary>
    public static DocumentStatus StatusAfterVersionUpload(DocumentStatus status) => status switch
    {
        DocumentStatus.Rejected => DocumentStatus.PendingReview,
        DocumentStatus.Created or DocumentStatus.PendingReview => status,
        _ => throw new InvalidOperationException(
            $"Cannot upload a new version while the document is {status}."),
    };
}
