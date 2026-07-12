using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ecert.DocsReview.Api.Infrastructure.OpenApi;

/// <summary>
/// Enriches the generated OpenAPI document so Swagger UI tells the review
/// story: request bodies come pre-assembled as named "Paso N" examples, form
/// fields are pre-filled, and endpoints are ordered by the lifecycle instead
/// of alphabetically.
/// </summary>
public static class StorylineOpenApi
{
    /// <summary>Lifecycle order for the paths; anything unknown sinks to the end.</summary>
    private static readonly string[] PathOrder =
    [
        "/api/documents",
        "/api/documents/{id}/status",
        "/api/documents/{id}/observations",
        "/api/documents/{id}/versions",
        "/api/documents/{id}/history",
        "/api/documents/{id}",
        "/api/documents/{id}/file",
        "/api/documents/{id}/versions/{versionNumber}/file",
    ];

    public static Task TransformDocumentAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "ecert Document Review API";
        document.Info.Description =
            "Los endpoints están ordenados por el ciclo de vida del documento, " +
            "y cada uno trae sus bodies pre-armados" +
            "se adjunta un PDF de ejemplo automáticamente";

        var ordered = document.Paths
            .OrderBy(p =>
            {
                var index = Array.IndexOf(PathOrder, p.Key);
                return index < 0 ? int.MaxValue : index;
            })
            .ToArray();
        document.Paths.Clear();
        foreach (var (path, item) in ordered)
        {
            document.Paths[path] = item;
        }

        // The live dashboard already lists every document (including the seeded
        // ones shown on startup), so GET /api/documents is redundant in the
        // tour. Drop the operation from the docs; the endpoint still exists and
        // the dashboard calls it directly.
        if (document.Paths.TryGetValue("/api/documents", out var docsPath)
            && docsPath.Operations is { } docsOperations)
        {
            docsOperations.Remove(HttpMethod.Get);
        }

        return Task.CompletedTask;
    }

    public static Task TransformOperationAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var key = $"{context.Description.HttpMethod} {context.Description.RelativePath?.TrimEnd('/')}";
        switch (key)
        {
            case "POST api/documents":
                Describe(operation,
                    "Paso 1 — Registre el documento con su primera versión. Los campos ya vienen " +
                    "pre-cargados y en `File` se adjunta un PDF de ejemplo automáticamente, así que " +
                    "basta con presionar Execute (o reemplazarlo por un PDF propio, p. ej. " +
                    "`samples/contrato-v1.pdf`). La respuesta incluye `pageCount` calculado con " +
                    "PdfPig; guarde el `id` para los pasos siguientes.");
                SetFormFieldExamples(operation, new Dictionary<string, string>
                {
                    ["Title"] = "Contrato Demo",
                    ["Type"] = "Contract",
                    ["UploadedBy"] = "juan.author",
                });
                break;

            case "POST api/documents/{id}/status":
                Describe(operation,
                    "Pasos 2, 3, 5 y 7 de la historia: en 'Try it out', el desplegable 'Examples' " +
                    "trae el body de cada paso ya armado. Rechazar exige `reason`, que queda " +
                    "registrado como observación; una transición inválida devuelve 409.");
                SetJsonExamples(operation, new (string Key, string Summary, JsonNode Body)[]
                {
                    ("paso-2-enviar-a-revision",
                        "Paso 2 — Enviar a revisión (Created → PendingReview)",
                        new JsonObject
                        {
                            ["targetStatus"] = "PendingReview",
                            ["performedBy"] = "juan.author",
                        }),
                    ("paso-3-tomar-la-revision",
                        "Paso 3 — Tomar la revisión (PendingReview → UnderReview)",
                        new JsonObject
                        {
                            ["targetStatus"] = "UnderReview",
                            ["performedBy"] = "maria.reviewer",
                        }),
                    ("paso-5-rechazar-con-motivo",
                        "Paso 5 — Rechazar con motivo obligatorio (UnderReview → Rejected)",
                        new JsonObject
                        {
                            ["targetStatus"] = "Rejected",
                            ["performedBy"] = "maria.reviewer",
                            ["reason"] = "Corregir plazo y precio antes de reenviar.",
                        }),
                    ("paso-7-aprobar",
                        "Paso 7 — Aprobar la versión corregida (UnderReview → Approved; repita antes el paso 3)",
                        new JsonObject
                        {
                            ["targetStatus"] = "Approved",
                            ["performedBy"] = "maria.reviewer",
                        }),
                });
                break;

            case "POST api/documents/{id}/observations":
                Describe(operation,
                    "Paso 4 — La revisora registra una solicitud de corrección sobre la versión " +
                    "vigente. Solo se permite dentro del bucle de revisión.");
                SetJsonExamples(operation, new (string Key, string Summary, JsonNode Body)[]
                {
                    ("paso-4-solicitud-de-correccion",
                        "Paso 4 — Solicitud de corrección",
                        new JsonObject
                        {
                            ["type"] = "CorrectionRequest",
                            ["content"] = "El plazo de la cláusula 2 no coincide con lo cotizado.",
                            ["createdBy"] = "maria.reviewer",
                        }),
                    ("comentario",
                        "Alternativa — Comentario simple",
                        new JsonObject
                        {
                            ["type"] = "Comment",
                            ["content"] = "Formato y numeración de cláusulas correctos.",
                            ["createdBy"] = "maria.reviewer",
                        }),
                });
                break;

            case "POST api/documents/{id}/versions":
                Describe(operation,
                    "Paso 6 — El autor sube la versión corregida: `File` ya trae un PDF de ejemplo " +
                    "adjunto (o reemplácelo por `samples/contrato-v2.pdf`). Un documento rechazado " +
                    "vuelve automáticamente a PendingReview; un archivo idéntico al vigente devuelve 400.");
                SetFormFieldExamples(operation, new Dictionary<string, string>
                {
                    ["UploadedBy"] = "juan.author",
                });
                break;

            case "GET api/documents/{id}/history":
                Describe(operation,
                    "Paso 8 — Trazabilidad: la auditoría completa del documento (creación, " +
                    "versiones, cambios de estado y observaciones) en orden cronológico.");
                break;

            case "GET api/documents/{id}/observations":
                Describe(operation,
                    "Paso 9 — Todas las observaciones registradas, indicando a qué versión " +
                    "pertenece cada una (incluido el motivo del rechazo del paso 5).");
                break;

            case "GET api/documents/{id}":
                Describe(operation,
                    "Paso 10 — El documento con su historial de versiones: estado final, versión " +
                    "vigente y las observaciones de cada versión.");
                break;

            case "GET api/documents/{id}/file":
            case "GET api/documents/{id}/versions/{versionNumber}/file":
                Describe(operation, "Extra — Descarga del PDF almacenado.");
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Prepends the story narrative, keeping any generated description.</summary>
    private static void Describe(OpenApiOperation operation, string narrative)
    {
        operation.Description = string.IsNullOrEmpty(operation.Description)
            ? narrative
            : $"{narrative}\n\n{operation.Description}";
    }

    /// <summary>
    /// Adds named examples to every JSON media type of the request body.
    /// Swagger UI shows them as an "Examples" dropdown with the summary as label.
    /// </summary>
    private static void SetJsonExamples(
        OpenApiOperation operation, IReadOnlyList<(string Key, string Summary, JsonNode Body)> examples)
    {
        if (operation.RequestBody?.Content is not { } content)
        {
            return;
        }

        foreach (var (mediaKey, mediaType) in content)
        {
            if (!mediaKey.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // DeepClone: a JsonNode can only have one parent, and the same body
            // is attached to several media types (application/json, text/json…).
            mediaType.Examples = examples.ToDictionary(
                e => e.Key,
                IOpenApiExample (e) => new OpenApiExample
                {
                    Summary = e.Summary,
                    Value = e.Body.DeepClone(),
                });
        }
    }

    /// <summary>Pre-fills multipart form fields via schema property examples.</summary>
    private static void SetFormFieldExamples(
        OpenApiOperation operation, IReadOnlyDictionary<string, string> values)
    {
        if (operation.RequestBody?.Content is not { } content
            || !content.TryGetValue("multipart/form-data", out var mediaType)
            || mediaType.Schema?.Properties is not { } properties)
        {
            return;
        }

        foreach (var (name, value) in values)
        {
            if (properties.TryGetValue(name, out var property) && property is OpenApiSchema schema)
            {
                schema.Example = JsonValue.Create(value);
            }
        }
    }
}
