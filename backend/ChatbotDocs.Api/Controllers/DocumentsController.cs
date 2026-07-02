using ChatbotDocs.Api.Models;
using ChatbotDocs.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace ChatbotDocs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private static readonly HashSet<string> AllowedUploadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".docx", ".csv", ".json"
    };

    private readonly IAnythingLlmClient _anythingLlmClient;
    private readonly IDocumentStorageService _documentStorage;
    private readonly IDocumentSyncService _documentSync;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IAnythingLlmClient anythingLlmClient,
        IDocumentStorageService documentStorage,
        IDocumentSyncService documentSync,
        ILogger<DocumentsController> logger)
    {
        _anythingLlmClient = anythingLlmClient;
        _documentStorage = documentStorage;
        _documentSync = documentSync;
        _logger = logger;
    }

    /// <summary>Lista las carpetas/casos disponibles dentro de Documentos/, para que el frontend arme la ruta.</summary>
    [HttpGet("carpetas")]
    public ActionResult<IReadOnlyList<string>> ListCarpetas()
    {
        return Ok(_documentSync.ListAvailableCarpetas());
    }

    /// <summary>
    /// Lista los documentos de un caso (los de su carpeta fija, los subidos por API, y los
    /// generados), indicando cuáles ya están embebidos (consultables) en su workspace de AnythingLLM.
    /// </summary>
    [HttpGet("{carpeta}")]
    public async Task<ActionResult<IReadOnlyList<DocumentDto>>> List(string carpeta, CancellationToken cancellationToken)
    {
        try
        {
            var carpetaSlug = SlugHelper.Sanitize(carpeta);
            var workspaceSlug = await _anythingLlmClient.EnsureWorkspaceAsync(carpetaSlug, cancellationToken);
            var embeddedTitles = await SafeListEmbeddedTitlesAsync(workspaceSlug, cancellationToken);

            var documents = _documentSync.ListSourceDocuments(carpetaSlug, embeddedTitles)
                .Concat(_documentStorage.ListDocuments(carpetaSlug, embeddedTitles))
                .OrderByDescending(doc => doc.CreatedAtUtc)
                .ToList();

            return Ok(documents);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Escanea la carpeta fija del caso (Documentos/{carpeta}) y embebe + fija ("pin") en su
    /// workspace los archivos nuevos, para que el chat de ese caso razone sobre el contenido
    /// completo de todos ellos (p. ej. marco legal + formularios ingresados).
    /// </summary>
    [HttpPost("{carpeta}/sync")]
    public async Task<ActionResult<DocumentSyncResponse>> Sync(string carpeta, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _documentSync.SyncAsync(carpeta, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "Fallo al sincronizar el caso {Carpeta} con AnythingLLM", carpeta);
            return Problem(title: "No se pudo sincronizar la carpeta de documentos", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Sube un documento a un caso: lo guarda en el servidor y lo embebe en el workspace de esa
    /// carpeta para que su chat pueda responder preguntas sobre su contenido.
    /// </summary>
    [HttpPost("{carpeta}/upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<UploadDocumentResponse>> Upload(string carpeta, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Debes adjuntar un archivo.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedUploadExtensions.Contains(extension))
        {
            return BadRequest($"Extensión no soportada. Usa: {string.Join(", ", AllowedUploadExtensions)}");
        }

        string carpetaSlug;
        try
        {
            carpetaSlug = SlugHelper.Sanitize(carpeta);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        await using var readStreamForDisk = file.OpenReadStream();
        var savedFileName = await _documentStorage.SaveUploadedFileAsync(carpetaSlug, readStreamForDisk, file.FileName, cancellationToken);

        var embedded = false;
        var message = "Documento guardado en el servidor.";
        try
        {
            var workspaceSlug = await _anythingLlmClient.EnsureWorkspaceAsync(carpetaSlug, cancellationToken);
            await using var readStreamForAnythingLlm = file.OpenReadStream();
            var location = await _anythingLlmClient.UploadDocumentAsync(readStreamForAnythingLlm, file.FileName, cancellationToken);
            await _anythingLlmClient.EmbedDocumentsAsync(workspaceSlug, new[] { location }, cancellationToken);
            embedded = true;
            message = "Documento guardado y disponible para consultas en el chat.";
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "El documento se guardó en el servidor pero no se pudo embeber en AnythingLLM");
            message = "El documento se guardó en el servidor, pero no se pudo indexar en AnythingLLM: " + ex.Message;
        }

        return Ok(new UploadDocumentResponse
        {
            FileName = savedFileName,
            EmbeddedInWorkspace = embedded,
            Message = message
        });
    }

    /// <summary>
    /// Pide al modelo (vía AnythingLLM/Ollama) que redacte un documento para el caso indicado,
    /// lo guarda como Markdown en el servidor y, opcionalmente, lo embebe en el workspace de esa
    /// carpeta para poder preguntarle luego sobre lo que acaba de generar.
    /// </summary>
    [HttpPost("{carpeta}/generate")]
    public async Task<ActionResult<GenerateDocumentResponse>> Generate(
        string carpeta, [FromBody] GenerateDocumentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Debes indicar qué documento quieres generar.");
        }

        string carpetaSlug;
        try
        {
            carpetaSlug = SlugHelper.Sanitize(carpeta);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        string content;
        string workspaceSlug;
        try
        {
            workspaceSlug = await _anythingLlmClient.EnsureWorkspaceAsync(carpetaSlug, cancellationToken);

            var instruction =
                "Redacta el siguiente documento en formato Markdown, con títulos y estructura clara. " +
                "Devuelve únicamente el contenido del documento, sin comentarios adicionales.\n\n" +
                $"Petición: {request.Prompt}";

            var chatResponse = await _anythingLlmClient.ChatAsync(workspaceSlug, instruction, "chat", threadSlug: null, cancellationToken);
            content = chatResponse.Answer;
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "Fallo al generar el documento con AnythingLLM");
            return Problem(title: "No se pudo generar el documento", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        var savedFileName = await _documentStorage.SaveGeneratedDocumentAsync(
            carpetaSlug, request.FileName ?? SlugFromPrompt(request.Prompt), content, cancellationToken);

        var embedded = false;
        if (request.EmbedAfterGeneration)
        {
            try
            {
                await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                var location = await _anythingLlmClient.UploadDocumentAsync(stream, savedFileName, cancellationToken);
                await _anythingLlmClient.EmbedDocumentsAsync(workspaceSlug, new[] { location }, cancellationToken);
                embedded = true;
            }
            catch (AnythingLlmException ex)
            {
                _logger.LogError(ex, "El documento se generó pero no se pudo embeber de nuevo en AnythingLLM");
            }
        }

        return Ok(new GenerateDocumentResponse
        {
            FileName = savedFileName,
            Content = content,
            EmbeddedInWorkspace = embedded
        });
    }

    /// <summary>Descarga un documento (subido o generado) de un caso específico, por su nombre de archivo.</summary>
    [HttpGet("{carpeta}/download/{fileName}")]
    public IActionResult Download(string carpeta, string fileName)
    {
        string carpetaSlug;
        try
        {
            carpetaSlug = SlugHelper.Sanitize(carpeta);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        var physicalPath = _documentStorage.ResolvePhysicalPath(carpetaSlug, fileName);
        if (physicalPath is null)
        {
            return NotFound();
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        if (!contentTypeProvider.TryGetContentType(physicalPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return PhysicalFile(physicalPath, contentType, Path.GetFileName(physicalPath));
    }

    private async Task<IReadOnlySet<string>> SafeListEmbeddedTitlesAsync(string workspaceSlug, CancellationToken cancellationToken)
    {
        try
        {
            return await _anythingLlmClient.ListEmbeddedDocumentTitlesAsync(workspaceSlug, cancellationToken);
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar el estado de embeddings del workspace {Workspace}", workspaceSlug);
            return new HashSet<string>();
        }
    }

    private static string SlugFromPrompt(string prompt)
    {
        var slug = new string(prompt.Take(40).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return $"generado-{slug.Trim('-')}-{DateTime.UtcNow:yyyyMMddHHmmss}.md";
    }
}
