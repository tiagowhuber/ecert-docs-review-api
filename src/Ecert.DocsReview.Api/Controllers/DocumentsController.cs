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

    /// <summary>Uploads a new version of an existing document.</summary>
    [HttpPost("{id:guid}/versions")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadVersion(
        Guid id, [FromForm] UploadVersionRequest request, CancellationToken ct)
    {
        var result = await _documents.UploadVersionAsync(id, request, ct);
        switch (result.Error)
        {
            case UploadVersionError.NotFound:
                return Problem(
                    $"No document with id '{id}' exists.",
                    statusCode: StatusCodes.Status404NotFound);
            case UploadVersionError.VersionNotAllowed:
                return Problem(
                    $"Cannot upload a new version while the document is {result.CurrentStatus}.",
                    statusCode: StatusCodes.Status409Conflict);
            case UploadVersionError.None:
                return CreatedAtAction(
                    nameof(GetById), new { id = result.Document!.Id }, result.Document);
            default:
                ModelState.AddModelError(nameof(request.File), DescribeError(result.Error));
                return ValidationProblem(ModelState);
        }
    }

    /// <summary>Downloads the stored PDF of a specific version.</summary>
    [HttpGet("{id:guid}/versions/{versionNumber:int}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadVersionFile(
        Guid id, int versionNumber, CancellationToken ct)
    {
        var result = await _documents.GetVersionFileAsync(id, versionNumber, ct);
        return ToFileResponse(result, id, $"Document '{id}' has no version {versionNumber}.");
    }

    /// <summary>Downloads the stored PDF of the current (latest) version.</summary>
    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadCurrentFile(Guid id, CancellationToken ct)
    {
        var result = await _documents.GetVersionFileAsync(id, versionNumber: null, ct);
        return ToFileResponse(result, id, $"Document '{id}' has no versions.");
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

    /// <summary>Records an observation (comment or correction request) on the current version.</summary>
    [HttpPost("{id:guid}/observations")]
    [ProducesResponseType(typeof(ObservationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddObservation(
        Guid id, [FromBody] AddObservationRequest request, CancellationToken ct)
    {
        var result = await _documents.AddObservationAsync(id, request, ct);
        switch (result.Error)
        {
            case AddObservationError.NotFound:
                return Problem(
                    $"No document with id '{id}' exists.",
                    statusCode: StatusCodes.Status404NotFound);
            case AddObservationError.NotAllowed:
                return Problem(
                    $"Cannot add an observation while the document is {result.CurrentStatus}.",
                    statusCode: StatusCodes.Status409Conflict);
            case AddObservationError.RejectionReasonNotAllowed:
                ModelState.AddModelError(
                    nameof(request.Type),
                    "Rejection reasons are recorded by rejecting the document via the status endpoint.");
                return ValidationProblem(ModelState);
            default:
                return CreatedAtAction(
                    nameof(GetObservations), new { id }, result.Observation);
        }
    }

    /// <summary>Lists every observation recorded on the document, across all versions.</summary>
    [HttpGet("{id:guid}/observations")]
    [ProducesResponseType(typeof(IReadOnlyList<ObservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetObservations(Guid id, CancellationToken ct)
    {
        var observations = await _documents.GetObservationsAsync(id, ct);
        return observations is null
            ? Problem($"No document with id '{id}' exists.", statusCode: StatusCodes.Status404NotFound)
            : Ok(observations);
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

    private IActionResult ToFileResponse(GetFileResult result, Guid id, string versionNotFoundDetail)
    {
        return result.Error switch
        {
            GetFileError.DocumentNotFound => Problem(
                $"No document with id '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound),
            GetFileError.VersionNotFound => Problem(
                versionNotFoundDetail, statusCode: StatusCodes.Status404NotFound),
            _ => File(result.Content!, "application/pdf", result.FileName),
        };
    }

    private static string DescribeError(RegisterError error) => error switch
    {
        RegisterError.EmptyFile => "The uploaded file is empty.",
        RegisterError.FileTooLarge =>
            $"The file exceeds the maximum size of {DocumentService.MaxFileSizeBytes} bytes.",
        RegisterError.NotAPdf => "The uploaded file is not a valid PDF.",
        _ => "The uploaded file is invalid.",
    };

    private static string DescribeError(UploadVersionError error) => error switch
    {
        UploadVersionError.EmptyFile => "The uploaded file is empty.",
        UploadVersionError.FileTooLarge =>
            $"The file exceeds the maximum size of {DocumentService.MaxFileSizeBytes} bytes.",
        UploadVersionError.NotAPdf => "The uploaded file is not a valid PDF.",
        UploadVersionError.DuplicateContent =>
            "The uploaded file is identical to the current version.",
        _ => "The uploaded file is invalid.",
    };
}
