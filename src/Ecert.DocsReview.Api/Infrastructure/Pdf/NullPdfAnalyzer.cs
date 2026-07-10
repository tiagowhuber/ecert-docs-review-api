namespace Ecert.DocsReview.Api.Infrastructure.Pdf;

/// <summary>
/// Placeholder analyzer that performs no real inspection — page count stays
/// unknown. Replaced by a PdfPig-backed implementation in a later commit; the
/// rest of the pipeline already treats page count as optional.
/// </summary>
public class NullPdfAnalyzer : IPdfAnalyzer
{
    public Task<PdfAnalysis> AnalyzeAsync(Stream pdf, CancellationToken ct = default) =>
        Task.FromResult(new PdfAnalysis(PageCount: null));
}
