using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecert.DocsReview.Api.Domain;
using Ecert.DocsReview.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecert.DocsReview.Tests;

public class DocumentsApiTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DocumentsApiTests()
    {
        _fixture = new ApiTestFixture();
        _fixture.EnsureDatabaseCreated();
        _client = _fixture.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Register_WithValidPdf_Returns201WithDocument()
    {
        using var content = BuildRegisterForm("Service Contract", DocumentType.Contract, "juan.author");

        var response = await _client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var document = await ReadJsonAsync<DocumentDto>(response);
        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal("Service Contract", document.Title);
        Assert.Equal("Created", document.Status);
        Assert.Equal(1, document.CurrentVersionNumber);
        Assert.Single(document.Versions);
        Assert.Equal(1, document.Versions[0].VersionNumber);
    }

    [Fact]
    public async Task Register_PersistsFileAndEntities()
    {
        using var content = BuildRegisterForm("Quarterly Report", DocumentType.Report, "ana.author");

        var response = await _client.PostAsync("/api/documents", content);
        response.EnsureSuccessStatusCode();
        var document = await ReadJsonAsync<DocumentDto>(response);

        // File written under the storage root at {documentId}/v1.pdf.
        var expectedFile = Path.Combine(_fixture.StorageRoot, document.Id.ToString(), "v1.pdf");
        Assert.True(File.Exists(expectedFile), $"Expected stored PDF at {expectedFile}");

        // Document + version + two audit events persisted.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Versions)
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.Created, stored.Status);
        Assert.Single(stored.Versions);
        Assert.Contains(stored.Events, e => e.EventType == DocumentEventType.DocumentCreated);
        Assert.Contains(stored.Events, e => e.EventType == DocumentEventType.VersionUploaded);
    }

    [Fact]
    public async Task Register_WithParseablePdf_ExtractsPageCount()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Two Page Annex"), "Title" },
            { new StringContent(nameof(DocumentType.Annex)), "Type" },
            { new StringContent("juan.author"), "UploadedBy" },
        };
        var file = new ByteArrayContent(MinimalPdf.Create(pages: 2));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", "annex.pdf");

        var response = await _client.PostAsync("/api/documents", content);
        response.EnsureSuccessStatusCode();
        var created = await ReadJsonAsync<DocumentDto>(response);

        var detail = await ReadJsonAsync<DocumentDto>(
            await _client.GetAsync($"/api/documents/{created.Id}"));
        var version = Assert.Single(detail.Versions);
        Assert.Equal(2, version.PageCount);
    }

    [Fact]
    public async Task Register_WithNonPdfFile_Returns400()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Not a PDF"), "Title" },
            { new StringContent(nameof(DocumentType.Other)), "Type" },
            { new StringContent("juan.author"), "UploadedBy" },
        };
        var bytes = Encoding.ASCII.GetBytes("this is plain text, not a pdf");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", "notes.pdf");

        var response = await _client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithMissingTitle_Returns400()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(nameof(DocumentType.Contract)), "Type" },
            { new StringContent("juan.author"), "UploadedBy" },
        };
        var file = new ByteArrayContent(SampledPdfBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", "contract.pdf");

        var response = await _client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsDocumentWithVersions()
    {
        using var form = BuildRegisterForm("Annex A", DocumentType.Annex, "juan.author");
        var created = await ReadJsonAsync<DocumentDto>(await _client.PostAsync("/api/documents", form));

        var response = await _client.GetAsync($"/api/documents/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync<DocumentDto>(response);
        Assert.Equal(created.Id, document.Id);
        Assert.Single(document.Versions);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        await _client.PostAsync("/api/documents",
            BuildRegisterForm("Doc One", DocumentType.Contract, "juan.author"));
        await _client.PostAsync("/api/documents",
            BuildRegisterForm("Doc Two", DocumentType.Report, "ana.author"));

        var all = await ReadJsonAsync<List<DocumentSummaryDto>>(
            await _client.GetAsync("/api/documents"));
        Assert.Equal(2, all.Count);

        var created = await ReadJsonAsync<List<DocumentSummaryDto>>(
            await _client.GetAsync("/api/documents?status=Created"));
        Assert.Equal(2, created.Count);
        Assert.All(created, d => Assert.Equal("Created", d.Status));

        var approved = await ReadJsonAsync<List<DocumentSummaryDto>>(
            await _client.GetAsync("/api/documents?status=Approved"));
        Assert.Empty(approved);
    }

    private static MultipartFormDataContent BuildRegisterForm(
        string title, DocumentType type, string uploadedBy)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent(type.ToString()), "Type" },
            { new StringContent(uploadedBy), "UploadedBy" },
        };
        var file = new ByteArrayContent(SampledPdfBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", $"{title}.pdf");
        return content;
    }

    /// <summary>Minimal bytes that satisfy the %PDF- magic-number check.</summary>
    private static byte[] SampledPdfBytes() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize response: {json}");
    }

    private record DocumentDto(
        Guid Id,
        string Title,
        string Status,
        int? CurrentVersionNumber,
        List<VersionDto> Versions);

    private record VersionDto(int VersionNumber, string FileName, int? PageCount);

    private record DocumentSummaryDto(Guid Id, string Title, string Status);
}
