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

public class DocumentStatusApiTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DocumentStatusApiTests()
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
    public async Task ChangeStatus_ValidTransition_Returns200WithUpdatedDocument()
    {
        var document = await RegisterDocumentAsync();

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "PendingReview",
            performedBy = "juan.author",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadJsonAsync<DocumentDto>(response);
        Assert.Equal(document.Id, updated.Id);
        Assert.Equal("PendingReview", updated.Status);
    }

    [Fact]
    public async Task ChangeStatus_PersistsEventAndTouchesUpdatedAt()
    {
        var document = await RegisterDocumentAsync();

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "PendingReview",
            performedBy = "juan.author",
            details = "Ready for review",
        });
        response.EnsureSuccessStatusCode();

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.PendingReview, stored.Status);
        Assert.True(stored.UpdatedAt > stored.CreatedAt);

        var statusEvent = Assert.Single(
            stored.Events, e => e.EventType == DocumentEventType.StatusChanged);
        Assert.Equal(DocumentStatus.Created, statusEvent.FromStatus);
        Assert.Equal(DocumentStatus.PendingReview, statusEvent.ToStatus);
        Assert.Equal("juan.author", statusEvent.PerformedBy);
        Assert.Equal("Ready for review", statusEvent.Details);
    }

    [Fact]
    public async Task ChangeStatus_InvalidTransition_Returns409WithProblemDetail()
    {
        var document = await RegisterDocumentAsync();

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "Approved",
            performedBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Cannot move from Created to Approved", body);
    }

    [Fact]
    public async Task ChangeStatus_UnknownDocument_Returns404()
    {
        var response = await PostStatusAsync(Guid.NewGuid(), new
        {
            targetStatus = "PendingReview",
            performedBy = "juan.author",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_RejectWithReason_StoresObservationOnCurrentVersion()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "Rejected",
            performedBy = "maria.reviewer",
            reason = "Totals on page 2 do not match the annex.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Versions).ThenInclude(v => v.Observations)
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.Rejected, stored.Status);

        var version = Assert.Single(stored.Versions);
        var observation = Assert.Single(version.Observations);
        Assert.Equal(ObservationType.RejectionReason, observation.Type);
        Assert.Equal("Totals on page 2 do not match the annex.", observation.Content);
        Assert.Equal("maria.reviewer", observation.CreatedBy);

        Assert.Contains(stored.Events, e => e.EventType == DocumentEventType.ObservationAdded);
        Assert.Contains(stored.Events, e =>
            e.EventType == DocumentEventType.StatusChanged &&
            e.FromStatus == DocumentStatus.UnderReview &&
            e.ToStatus == DocumentStatus.Rejected);
    }

    [Fact]
    public async Task ChangeStatus_RejectWithoutReason_Returns400_AndStatusUnchanged()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "Rejected",
            performedBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.UnderReview, stored.Status);
        Assert.DoesNotContain(stored.Events, e => e.ToStatus == DocumentStatus.Rejected);
    }

    [Fact]
    public async Task ChangeStatus_MissingPerformedBy_Returns400()
    {
        var document = await RegisterDocumentAsync();

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "PendingReview",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_MissingTargetStatus_Returns400()
    {
        var document = await RegisterDocumentAsync();

        var response = await PostStatusAsync(document.Id, new
        {
            performedBy = "juan.author",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_ArchivedDocument_Returns409()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview", "Approved", "Archived");

        var response = await PostStatusAsync(document.Id, new
        {
            targetStatus = "PendingReview",
            performedBy = "juan.author",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<DocumentDto> RegisterDocumentAsync()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Status Flow Contract"), "Title" },
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

    private Task<HttpResponseMessage> PostStatusAsync(Guid id, object body) =>
        _client.PostAsJsonAsync($"/api/documents/{id}/status", body);

    /// <summary>Walks the document through a chain of valid transitions.</summary>
    private async Task AdvanceAsync(Guid id, params string[] statuses)
    {
        foreach (var status in statuses)
        {
            var response = await PostStatusAsync(id, new
            {
                targetStatus = status,
                performedBy = "maria.reviewer",
            });
            response.EnsureSuccessStatusCode();
        }
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

    private record DocumentDto(Guid Id, string Title, string Status, int? CurrentVersionNumber);
}
