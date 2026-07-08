namespace Ecert.DocsReview.Api.Domain;

public enum DocumentStatus
{
    Created,
    PendingReview,
    UnderReview,
    Approved,
    Rejected,
    Archived
}
