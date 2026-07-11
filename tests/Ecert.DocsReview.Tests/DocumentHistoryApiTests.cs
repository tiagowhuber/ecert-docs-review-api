using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Tests;

public class DocumentHistoryApiTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DocumentHistoryApiTests()
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
    public async Task GetHistory_FreshDocument_ReturnsCreationAndFirstUploadInOrder()
    {
        var document = await RegisterDocumentAsync();

        var response = await _client.GetAsync($"/api/documents/{document.Id}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await ReadJsonAsync<List<EventDto>>(response);

        Assert.Equal(2, events.Count);
        Assert.Equal("DocumentCreated", events[0].EventType);
        Assert.Equal("VersionUploaded", events[1].EventType);
        Assert.All(events, e =>
        {
            Assert.Equal("juan.author", e.PerformedBy);
            Assert.Null(e.FromStatus);
            Assert.Null(e.ToStatus);
        });
    }

    [Fact]
    public async Task GetHistory_FullLifecycle_TellsTheWholeStoryInOrder()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");
        await RejectAsync(document.Id);
        var upload = await UploadVersionAsync(
            document.Id, SampledPdfBytes("revised"), "revised.pdf");
        upload.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/documents/{document.Id}/history");
        response.EnsureSuccessStatusCode();
        var events = await ReadJsonAsync<List<EventDto>>(response);

        var expected = new[]
        {
            ("DocumentCreated", (string?)null, (string?)null),
            ("VersionUploaded", (string?)null, (string?)null),
            ("StatusChanged", (string?)"Created", (string?)"PendingReview"),
            ("StatusChanged", (string?)"PendingReview", (string?)"UnderReview"),
            ("ObservationAdded", (string?)null, (string?)null),
            ("StatusChanged", (string?)"UnderReview", (string?)"Rejected"),
            ("VersionUploaded", (string?)null, (string?)null),
            ("StatusChanged", (string?)"Rejected", (string?)"PendingReview"),
        };

        Assert.Equal(
            expected,
            events.Select(e => (e.EventType, e.FromStatus, e.ToStatus)).ToArray());
    }

    [Fact]
    public async Task GetHistory_SameTimestampEvents_KeepInsertionOrder()
    {
        // Registration writes DocumentCreated and VersionUploaded with the
        // same OccurredAt; ordering must fall back to insertion order.
        var document = await RegisterDocumentAsync();

        var response = await _client.GetAsync($"/api/documents/{document.Id}/history");
        response.EnsureSuccessStatusCode();
        var events = await ReadJsonAsync<List<EventDto>>(response);

        Assert.Equal(events[0].OccurredAt, events[1].OccurredAt);
        Assert.Equal("DocumentCreated", events[0].EventType);
        Assert.True(events[0].Id < events[1].Id);
    }

    [Fact]
    public async Task GetHistory_UnknownDocument_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<DocumentDto> RegisterDocumentAsync()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Traceable Contract"), "Title" },
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
            { new StringContent("juan.author"), "UploadedBy" },
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

    private record DocumentDto(Guid Id, string Title, string Status, int? CurrentVersionNumber);

    private record EventDto(
        long Id,
        string EventType,
        string? FromStatus,
        string? ToStatus,
        string? Details,
        string PerformedBy,
        DateTimeOffset OccurredAt);
}
