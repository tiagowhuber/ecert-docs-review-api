using System.Net;

namespace Ecert.DocsReview.Tests;

/// <summary>
/// The OpenAPI document and the Scalar UI are part of the deliverable (the
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
    public async Task ScalarUi_IsExposed()
    {
        var response = await _client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
