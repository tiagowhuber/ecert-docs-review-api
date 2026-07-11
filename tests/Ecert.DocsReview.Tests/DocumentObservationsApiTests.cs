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

public class DocumentObservationsApiTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DocumentObservationsApiTests()
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
    public async Task AddObservation_CommentOnDocumentUnderReview_Returns201WithObservation()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            content = "Clause 4 wording looks fine.",
            createdBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var observation = await ReadJsonAsync<ObservationDto>(response);
        Assert.Equal("Comment", observation.Type);
        Assert.Equal("Clause 4 wording looks fine.", observation.Content);
        Assert.Equal("maria.reviewer", observation.CreatedBy);
        Assert.Equal(1, observation.VersionNumber);
    }

    [Fact]
    public async Task AddObservation_CorrectionRequest_PersistsObservationAndEvent()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostObservationAsync(document.Id, new
        {
            type = "CorrectionRequest",
            content = "Please update the totals on page 2.",
            createdBy = "maria.reviewer",
        });
        response.EnsureSuccessStatusCode();

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Versions).ThenInclude(v => v.Observations)
            .Include(d => d.Events)
            .SingleAsync(d => d.Id == document.Id);

        var version = Assert.Single(stored.Versions);
        var observation = Assert.Single(version.Observations);
        Assert.Equal(ObservationType.CorrectionRequest, observation.Type);
        Assert.Equal("Please update the totals on page 2.", observation.Content);
        Assert.Equal("maria.reviewer", observation.CreatedBy);

        Assert.Contains(stored.Events, e =>
            e.EventType == DocumentEventType.ObservationAdded &&
            e.PerformedBy == "maria.reviewer");
        Assert.True(stored.UpdatedAt > stored.CreatedAt);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Approved")]
    [InlineData("Archived")]
    public async Task AddObservation_OutsideReviewStates_Returns409(string state)
    {
        var document = await RegisterDocumentAsync();
        string[] path = state switch
        {
            "Created" => [],
            "Approved" => ["PendingReview", "UnderReview", "Approved"],
            _ => ["PendingReview", "UnderReview", "Approved", "Archived"],
        };
        await AdvanceAsync(document.Id, path);

        var response = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            content = "Too late for comments.",
            createdBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(state, body);
    }

    [Fact]
    public async Task AddObservation_RejectionReasonType_Returns400()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostObservationAsync(document.Id, new
        {
            type = "RejectionReason",
            content = "Trying to sneak in a rejection.",
            createdBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddObservation_MissingContent_Returns400()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            createdBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddObservation_MissingCreatedBy_Returns400()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            content = "Anonymous comment.",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddObservation_UnknownDocument_Returns404()
    {
        var response = await PostObservationAsync(Guid.NewGuid(), new
        {
            type = "Comment",
            content = "Nobody home.",
            createdBy = "maria.reviewer",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddObservation_AttachesToCurrentVersion()
    {
        var document = await RegisterDocumentAsync();
        var upload = await UploadVersionAsync(
            document.Id, SampledPdfBytes("revised"), "revised.pdf");
        upload.EnsureSuccessStatusCode();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var response = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            content = "Second version looks better.",
            createdBy = "maria.reviewer",
        });
        response.EnsureSuccessStatusCode();

        var observation = await ReadJsonAsync<ObservationDto>(response);
        Assert.Equal(2, observation.VersionNumber);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Documents
            .Include(d => d.Versions).ThenInclude(v => v.Observations)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Empty(stored.Versions.Single(v => v.VersionNumber == 1).Observations);
        Assert.Single(stored.Versions.Single(v => v.VersionNumber == 2).Observations);
    }

    [Fact]
    public async Task GetObservations_IncludesRejectionReasonFromStatusTransition()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");
        await RejectAsync(document.Id);

        var commentResponse = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            content = "Will fix in the next version.",
            createdBy = "juan.author",
        });
        commentResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/documents/{document.Id}/observations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var observations = await ReadJsonAsync<List<ObservationDto>>(response);
        Assert.Equal(2, observations.Count);
        Assert.Contains(observations, o =>
            o.Type == "RejectionReason" &&
            o.Content == "Totals on page 2 do not match the annex." &&
            o.VersionNumber == 1);
        Assert.Contains(observations, o =>
            o.Type == "Comment" && o.CreatedBy == "juan.author" && o.VersionNumber == 1);
    }

    [Fact]
    public async Task GetObservations_UnknownDocument_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/observations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetObservations_DocumentWithoutObservations_ReturnsEmptyList()
    {
        var document = await RegisterDocumentAsync();

        var response = await _client.GetAsync($"/api/documents/{document.Id}/observations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var observations = await ReadJsonAsync<List<ObservationDto>>(response);
        Assert.Empty(observations);
    }

    [Fact]
    public async Task GetDocument_NestsObservationsInsideVersions()
    {
        var document = await RegisterDocumentAsync();
        await AdvanceAsync(document.Id, "PendingReview", "UnderReview");

        var post = await PostObservationAsync(document.Id, new
        {
            type = "Comment",
            content = "Nested where it belongs.",
            createdBy = "maria.reviewer",
        });
        post.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/documents/{document.Id}");
        response.EnsureSuccessStatusCode();

        var detail = await ReadJsonAsync<DocumentDto>(response);
        var version = Assert.Single(detail.Versions);
        var observation = Assert.Single(version.Observations);
        Assert.Equal("Comment", observation.Type);
        Assert.Equal("Nested where it belongs.", observation.Content);
    }

    private async Task<DocumentDto> RegisterDocumentAsync()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Observed Contract"), "Title" },
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

    private Task<HttpResponseMessage> PostObservationAsync(Guid id, object body) =>
        _client.PostAsJsonAsync($"/api/documents/{id}/observations", body);

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

    private record VersionDto(int VersionNumber, string FileName, List<ObservationDto> Observations);

    private record ObservationDto(
        Guid Id,
        int VersionNumber,
        string Type,
        string Content,
        string CreatedBy,
        DateTimeOffset CreatedAt);
}
