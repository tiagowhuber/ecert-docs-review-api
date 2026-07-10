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

    /// <summary>Moves a document to a new lifecycle status.</summary>
    [HttpPost("{id:guid}/status")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(
        Guid id, [FromBody] ChangeDocumentStatusRequest request, CancellationToken ct)
    {
        var result = await _documents.ChangeStatusAsync(id, request, ct);
        switch (result.Error)
        {
            case ChangeStatusError.NotFound:
                return Problem(
                    $"No document with id '{id}' exists.",
                    statusCode: StatusCodes.Status404NotFound);
            case ChangeStatusError.InvalidTransition:
                return Problem(
                    $"Cannot move from {result.FromStatus} to {request.TargetStatus}.",
                    statusCode: StatusCodes.Status409Conflict);
            case ChangeStatusError.MissingRejectionReason:
                ModelState.AddModelError(
                    nameof(request.Reason), "A reason is required when rejecting a document.");
                return ValidationProblem(ModelState);
            default:
                return Ok(result.Document);
        }
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
