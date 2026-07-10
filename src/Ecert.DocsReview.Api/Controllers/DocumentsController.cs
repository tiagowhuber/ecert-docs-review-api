using Ecert.DocsReview.Api.Application;
using Ecert.DocsReview.Api.Contracts;
using Ecert.DocsReview.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Ecert.DocsReview.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documents;

    public DocumentsController(DocumentService documents)
    {
        _documents = documents;
    }

    /// <summary>Registers a new document together with its first PDF version.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromForm] RegisterDocumentRequest request, CancellationToken ct)
    {
        var result = await _documents.RegisterAsync(request, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(request.File), DescribeError(result.Error));
            return ValidationProblem(ModelState);
        }

        return CreatedAtAction(
            nameof(GetById), new { id = result.Document!.Id }, result.Document);
    }

    /// <summary>Returns a document with its full version history.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var document = await _documents.GetByIdAsync(id, ct);
        return document is null
            ? Problem($"No document with id '{id}' exists.", statusCode: StatusCodes.Status404NotFound)
            : Ok(document);
    }

    /// <summary>Lists documents, optionally filtered by status and/or type.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DocumentStatus? status, [FromQuery] DocumentType? type, CancellationToken ct)
    {
        var documents = await _documents.ListAsync(status, type, ct);
        return Ok(documents);
    }

    private static string DescribeError(RegisterError error) => error switch
    {
        RegisterError.EmptyFile => "The uploaded file is empty.",
        RegisterError.FileTooLarge =>
            $"The file exceeds the maximum size of {DocumentService.MaxFileSizeBytes} bytes.",
        RegisterError.NotAPdf => "The uploaded file is not a valid PDF.",
        _ => "The uploaded file is invalid.",
    };
}
