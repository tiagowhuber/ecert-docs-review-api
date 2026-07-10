namespace Ecert.DocsReview.Api.Infrastructure.Storage;

/// <summary>Bound from the <c>Storage</c> configuration section.</summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = string.Empty;
}
