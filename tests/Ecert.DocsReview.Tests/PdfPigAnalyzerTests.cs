using System.Text;
using Ecert.DocsReview.Api.Infrastructure.Pdf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecert.DocsReview.Tests;

public class PdfPigAnalyzerTests
{
    private readonly PdfPigAnalyzer _analyzer = new(NullLogger<PdfPigAnalyzer>.Instance);

    [Fact]
    public async Task AnalyzeAsync_SinglePagePdf_ReturnsPageCount1()
    {
        using var pdf = new MemoryStream(MinimalPdf.Create(pages: 1));

        var analysis = await _analyzer.AnalyzeAsync(pdf);

        Assert.Equal(1, analysis.PageCount);
    }

    [Fact]
    public async Task AnalyzeAsync_ThreePagePdf_ReturnsPageCount3()
    {
        using var pdf = new MemoryStream(MinimalPdf.Create(pages: 3));

        var analysis = await _analyzer.AnalyzeAsync(pdf);

        Assert.Equal(3, analysis.PageCount);
    }

    [Fact]
    public async Task AnalyzeAsync_MagicBytesButUnparseable_ReturnsNullWithoutThrowing()
    {
        // The shape the integration tests upload: passes the API's %PDF-
        // validation but is not a parseable document.
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");
        using var pdf = new MemoryStream(bytes);

        var analysis = await _analyzer.AnalyzeAsync(pdf);

        Assert.Null(analysis.PageCount);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyStream_ReturnsNullWithoutThrowing()
    {
        using var pdf = new MemoryStream();

        var analysis = await _analyzer.AnalyzeAsync(pdf);

        Assert.Null(analysis.PageCount);
    }
}
