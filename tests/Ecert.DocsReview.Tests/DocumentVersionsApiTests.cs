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

public class DocumentVersionsApiTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DocumentVersionsApiTests()
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
    public async Task UploadVersion_OnCreatedDocument_Returns201WithNewVersion()
    {
        var document = await RegisterDocumentAsync();

        var response = await UploadVersionAsync(
            document.Id, SampledPdfBytes("revised"), "revised.pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var updated = await ReadJsonAsync<DocumentDto>(response);
        Assert.Equal(2, updated.CurrentVersionNumber);
        Assert.Equal(2, updated.Versions.Count);
        Assert.Equal("Created", updated.Status);
    }

    [Fact]
    public async Task UploadVersion_PersistsFileEntitiesAndEvent()
    {
        var document = await RegisterDocumentAsync();
        var bytes = SampledPdfBytes("revised");

        var response = await UploadVersionAsync(document.Id, bytes, "revised.pdf");
        response.EnsureSuccessStatusCode();

        var expectedFile = Path.Combine(_fixture.StorageRoot, document.Id.ToString(), "v2.pdf");
        Assert.True(File.Exists(expectedFile), $"Expected stored PDF at {expectedFile}");

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Versions)
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(2, stored.Versions.Count);
        var v2 = stored.Versions.Single(v => v.VersionNumber == 2);
        Assert.Equal("revised.pdf", v2.FileName);
        Assert.Equal("ana.author", v2.UploadedBy);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            v2.Sha256);

        Assert.Equal(2, stored.Events.Count(e => e.EventType == DocumentEventType.VersionUploaded));
        Assert.DoesNotContain(stored.Events, e => e.EventType == DocumentEventType.StatusChanged);
        Assert.True(stored.UpdatedAt > stored.CreatedAt);
    }

    [Fact]
    public async Task UploadVersion_OnRejectedDocument_MovesToPendingReview()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");
        await RejectAsync(document.Id);

        var response = await UploadVersionAsync(
            document.Id, SampledPdfBytes("fixed totals"), "fixed.pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updated = await ReadJsonAsync<DocumentDto>(response);
        Assert.Equal("PendingReview", updated.Status);
        Assert.Equal(2, updated.CurrentVersionNumber);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Contains(stored.Events, e =>
            e.EventType == DocumentEventType.StatusChanged &&
            e.FromStatus == DocumentStatus.Rejected &&
            e.ToStatus == DocumentStatus.PendingReview);
        Assert.Equal(2, stored.Events.Count(e => e.EventType == DocumentEventType.VersionUploaded));
    }

    [Fact]
    public async Task UploadVersion_WhileUnderReview_Returns409()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await UploadVersionAsync(
            document.Id, SampledPdfBytes("revised"), "revised.pdf");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("UnderReview", body);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.DocumentVersions.CountAsync(v => v.DocumentId == document.Id));
    }

    [Fact]
    public async Task UploadVersion_OnArchivedDocument_Returns409()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview", "Approved", "Archived");

        var response = await UploadVersionAsync(
            document.Id, SampledPdfBytes("revised"), "revised.pdf");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_UnknownDocument_Returns404()
    {
        var response = await UploadVersionAsync(
            Guid.NewGuid(), SampledPdfBytes(), "any.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_IdenticalToCurrentVersion_Returns400()
    {
        var document = await RegisterDocumentAsync();

        // Same bytes as the registered v1 file.
        var response = await UploadVersionAsync(document.Id, SampledPdfBytes(), "copy.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("identical", body);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.DocumentVersions.CountAsync(v => v.DocumentId == document.Id));
    }

    [Fact]
    public async Task UploadVersion_NonPdfFile_Returns400()
    {
        var document = await RegisterDocumentAsync();
        var bytes = Encoding.ASCII.GetBytes("this is plain text, not a pdf");

        var response = await UploadVersionAsync(document.Id, bytes, "notes.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_EmptyFile_Returns400()
    {
        var document = await RegisterDocumentAsync();

        var response = await UploadVersionAsync(document.Id, [], "empty.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_MissingUploadedBy_Returns400()
    {
        var document = await RegisterDocumentAsync();
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(SampledPdfBytes("revised"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", "revised.pdf");

        var response = await _client.PostAsync(
            $"/api/documents/{document.Id}/versions", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersionFile_ReturnsPdfBytes()
    {
        var document = await RegisterDocumentAsync();

        var response = await _client.GetAsync(
            $"/api/documents/{document.Id}/versions/1/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("contract.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(SampledPdfBytes(), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DownloadVersionFile_UnknownVersion_Returns404()
    {
        var document = await RegisterDocumentAsync();

        var response = await _client.GetAsync(
            $"/api/documents/{document.Id}/versions/99/file");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersionFile_UnknownDocument_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/documents/{Guid.NewGuid()}/versions/1/file");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadCurrentFile_ReturnsLatestVersionBytes()
    {
        var document = await RegisterDocumentAsync();
        var v2Bytes = SampledPdfBytes("second version");
        (await UploadVersionAsync(document.Id, v2Bytes, "revised.pdf")).EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/documents/{document.Id}/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("revised.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(v2Bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DownloadCurrentFile_UnknownDocument_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/file");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<DocumentDto> RegisterDocumentAsync()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Versioned Contract"), "Title" },
            { new StringContent(nameof(DocumentType.Contract)), "Type" },
            { new StringContent("juan.author"), "UploadedBy" },
        };
        var file = new ByteArrayContent(SampledPdfBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", "contract.pdf");

        var response = await _client.PostAsync("/api/documents", content);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<DocumentDto>(response);
    }

    private Task<HttpResponseMessage> UploadVersionAsync(Guid id, byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent("ana.author"), "UploadedBy" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", fileName);
        return _client.PostAsync($"/api/documents/{id}/versions", content);
    }

    /// <summary>Walks the document through a chain of valid transitions.</summary>
    private async Task AdvanceAsync(Guid id, params string[] statuses)
    {
        foreach (var status in statuses)
        {
            var response = await _client.PostAsJsonAsync($"/api/documents/{id}/status", new
            {
                targetStatus = status,
                performedBy = "maria.reviewer",
            });
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task RejectAsync(Guid id)
    {
        var response = await _client.PostAsJsonAsync($"/api/documents/{id}/status", new
        {
            targetStatus = "Rejected",
            performedBy = "maria.reviewer",
            reason = "Totals on page 2 do not match the annex.",
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Minimal bytes that satisfy the %PDF- magic-number check. The suffix
    /// varies the content so a second upload has a different SHA-256.
    /// </summary>
    private static byte[] SampledPdfBytes(string suffix = "") =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n{suffix}\n%%EOF");

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

    private record VersionDto(int VersionNumber, string FileName);
}
