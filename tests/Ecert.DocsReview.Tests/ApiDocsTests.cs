using System.Net;
using System.Text.Json;

namespace Ecert.DocsReview.Tests;

/// <summary>
/// The OpenAPI document and the Swagger UI are part of the deliverable (the
/// examiner explores the API through them), so their availability is smoke
/// tested like any other endpoint.
/// </summary>
public class ApiDocsTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    public ApiDocsTests()
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
    public async Task OpenApiDocument_IsExposed_AndDescribesTheDocumentsApi()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/documents", json);
    }

    [Fact]
    public async Task SwaggerUi_IsExposed()
    {
        var response = await _client.GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApiDocument_TellsTheReviewStoryline_WithNamedExamples()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        // Named request-body examples walk the reviewer through the lifecycle.
        Assert.Contains("Paso 2", json);
        Assert.Contains("Paso 5", json);
        Assert.Contains("Corregir plazo", json);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesEnumsAsStrings()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        // The API serializes enums by name, so the document must list the
        // member names, not describe the fields as opaque integers.
        var type = GetDocumentTypeFormFieldSchema(json);
        var members = type.GetProperty("enum").EnumerateArray().ToArray();
        Assert.Contains(members, m => m.ValueKind == JsonValueKind.String && m.GetString() == "Contract");
        Assert.DoesNotContain(members, m => m.ValueKind == JsonValueKind.Number);
        // Statuses too (they only appear by name when serialized as strings).
        Assert.Contains("PendingReview", json);
    }

    [Fact]
    public async Task OpenApiDocument_PrefillsTheDocumentTypeFormField()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        var type = GetDocumentTypeFormFieldSchema(json);
        Assert.Equal("Contract", type.GetProperty("example").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_OmitsListOperation_KeepsCreate()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        // The live dashboard already lists documents, so GET /api/documents is
        // dropped from the docs; POST (create) stays.
        using var doc = JsonDocument.Parse(json);
        var verbs = doc.RootElement
            .GetProperty("paths").GetProperty("/api/documents")
            .EnumerateObject().Select(p => p.Name).ToList();

        Assert.DoesNotContain("get", verbs);
        Assert.Contains("post", verbs);
    }

    /// <summary>The `Type` form field of POST /api/documents in the OpenAPI JSON.</summary>
    private static JsonElement GetDocumentTypeFormFieldSchema(string openApiJson)
    {
        using var doc = JsonDocument.Parse(openApiJson);
        return doc.RootElement
            .GetProperty("paths").GetProperty("/api/documents")
            .GetProperty("post").GetProperty("requestBody")
            .GetProperty("content").GetProperty("multipart/form-data")
            .GetProperty("schema").GetProperty("properties").GetProperty("Type")
            .Clone();
    }
}
