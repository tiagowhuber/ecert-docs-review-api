namespace Ecert.DocsReview.Api.Domain;

public class Observation
{
    public Guid Id { get; set; }
    public Guid DocumentVersionId { get; set; }
    public DocumentVersion? DocumentVersion { get; set; }

    public ObservationType Type { get; set; }
    public required string Content { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
