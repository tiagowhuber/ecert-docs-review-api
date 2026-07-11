using UglyToad.PdfPig;

namespace Ecert.DocsReview.Api.Infrastructure.Pdf;

/// <summary>
/// PDF analysis backed by PdfPig (UglyToad.PdfPig), chosen because it is a
/// mature open-source .NET library that runs fully locally: no API keys, no
/// network dependency, identical behavior inside the Docker container. The
/// <see cref="IPdfAnalyzer"/> seam keeps it swappable for a remote OCR or
/// analysis API without touching application logic.
/// </summary>
public class PdfPigAnalyzer : IPdfAnalyzer
{
    private readonly ILogger<PdfPigAnalyzer> _logger;

    public PdfPigAnalyzer(ILogger<PdfPigAnalyzer> logger)
    {
        _logger = logger;
    }

    public Task<PdfAnalysis> AnalyzeAsync(Stream pdf, CancellationToken ct = default)
    {
        // Page count is optional metadata: a file that passed the upload
        // validation but that PdfPig cannot parse is stored anyway, with the
        // count left unknown. The broad catch is deliberate — PdfPig throws
        // assorted exception types for malformed files, and every one of them
        // means the same thing here.
        try
        {
            using var document = PdfDocument.Open(pdf);
            return Task.FromResult(new PdfAnalysis(document.NumberOfPages));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PDF analysis failed; storing the version without a page count.");
            return Task.FromResult(new PdfAnalysis(PageCount: null));
        }
    }
}
