namespace Ecert.DocsReview.Api.Infrastructure.Pdf;

/// <summary>Result of analyzing a PDF's content.</summary>
public record PdfAnalysis(int? PageCount);

/// <summary>
/// External-integration seam for PDF processing (page count, text extraction,
/// …). Kept behind an interface so the concrete library — PdfPig, an OCR
/// service, etc. — can be swapped without touching application logic.
/// </summary>
public interface IPdfAnalyzer
{
    Task<PdfAnalysis> AnalyzeAsync(Stream pdf, CancellationToken ct = default);
}
