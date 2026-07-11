using System.Text;

namespace Ecert.DocsReview.Tests;

/// <summary>
/// Builds tiny but structurally valid PDFs (correct xref offsets) with a
/// chosen number of pages, so tests can exercise a real PDF parser. Same
/// technique as the seeder's single-page builder. Text must be plain ASCII
/// without parentheses or backslashes.
/// </summary>
public static class MinimalPdf
{
    public static byte[] Create(int pages, string text = "Test document")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);

        // Object layout: 1 catalog, 2 pages tree, then a (page, content
        // stream) pair per page, and the shared font object last.
        var fontObjectNumber = 3 + 2 * pages;
        var kids = string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{3 + 2 * i} 0 R"));

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{kids}] /Count {pages} >>",
        };
        for (var i = 0; i < pages; i++)
        {
            var content = $"BT /F1 18 Tf 72 720 Td (Page {i + 1}: {text}) Tj ET";
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                $"/Contents {4 + 2 * i} 0 R /Resources << /Font << /F1 {fontObjectNumber} 0 R >> >> >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = sb.Length;
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append($"{offset:D10} 00000 n \n");
        }
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
