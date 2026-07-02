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

    /// <summary>
    /// Lista los documentos que hay en el servidor (subidos por API, generados, y los de la
    /// carpeta fija), indicando cuáles ya están embebidos (consultables) en AnythingLLM.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentDto>>> List(CancellationToken cancellationToken)
    {
        var embeddedTitles = await SafeListKnownTitlesAsync(cancellationToken);

        var documents = _documentStorage.ListDocuments(embeddedTitles)
            .Concat(_documentSync.ListSourceDocuments(embeddedTitles))
            .OrderByDescending(doc => doc.CreatedAtUtc)
            .ToList();

        return Ok(documents);
    }

    /// <summary>
    /// Escanea la carpeta fija de documentos del servidor (configurada en DocumentSync:FolderPath,
    /// por defecto "Documentos") y embebe + fija ("pin") en AnythingLLM los archivos nuevos, para
    /// que el chat pueda responder preguntas que cruzan el contenido de varios documentos completos
    /// (p. ej. marco legal + formularios ingresados).
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<DocumentSyncResponse>> Sync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _documentSync.SyncAsync(cancellationToken);
            return Ok(result);
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "Fallo al sincronizar la carpeta de documentos con AnythingLLM");
            return Problem(title: "No se pudo sincronizar la carpeta de documentos", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Sube un documento: lo guarda en el servidor y lo embebe (vectoriza) en el workspace
    /// de AnythingLLM para que el chat pueda responder preguntas sobre su contenido.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<UploadDocumentResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
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

        await using var readStreamForDisk = file.OpenReadStream();
        var savedFileName = await _documentStorage.SaveUploadedFileAsync(readStreamForDisk, file.FileName, cancellationToken);

        var embedded = false;
        var message = "Documento guardado en el servidor.";
        try
        {
            await using var readStreamForAnythingLlm = file.OpenReadStream();
            var location = await _anythingLlmClient.UploadDocumentAsync(readStreamForAnythingLlm, file.FileName, cancellationToken);
            await _anythingLlmClient.EmbedDocumentsAsync(new[] { location }, cancellationToken);
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
    /// Pide al modelo (vía AnythingLLM/Ollama) que redacte un documento a partir de una instrucción,
    /// lo guarda como Markdown en el servidor y, opcionalmente, lo vuelve a embeber para poder
    /// preguntarle luego sobre lo que acaba de generar.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GenerateDocumentResponse>> Generate(
        [FromBody] GenerateDocumentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Debes indicar qué documento quieres generar.");
        }

        string content;
        try
        {
            var instruction =
                "Redacta el siguiente documento en formato Markdown, con títulos y estructura clara. " +
                "Devuelve únicamente el contenido del documento, sin comentarios adicionales.\n\n" +
                $"Petición: {request.Prompt}";

            var chatResponse = await _anythingLlmClient.ChatAsync(instruction, "chat", threadSlug: null, cancellationToken);
            content = chatResponse.Answer;
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "Fallo al generar el documento con AnythingLLM");
            return Problem(title: "No se pudo generar el documento", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        var savedFileName = await _documentStorage.SaveGeneratedDocumentAsync(
            request.FileName ?? SlugFromPrompt(request.Prompt), content, cancellationToken);

        var embedded = false;
        if (request.EmbedAfterGeneration)
        {
            try
            {
                await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                var location = await _anythingLlmClient.UploadDocumentAsync(stream, savedFileName, cancellationToken);
                await _anythingLlmClient.EmbedDocumentsAsync(new[] { location }, cancellationToken);
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

    /// <summary>Descarga un documento (subido o generado) por su nombre de archivo.</summary>
    [HttpGet("download/{fileName}")]
    public IActionResult Download(string fileName)
    {
        var physicalPath = _documentStorage.ResolvePhysicalPath(fileName);
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

    private async Task<IReadOnlySet<string>> SafeListKnownTitlesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _anythingLlmClient.ListKnownDocumentTitlesAsync(cancellationToken);
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar el estado de embeddings en AnythingLLM");
            return new HashSet<string>();
        }
    }

    private static string SlugFromPrompt(string prompt)
    {
        var slug = new string(prompt.Take(40).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return $"generado-{slug.Trim('-')}-{DateTime.UtcNow:yyyyMMddHHmmss}.md";
    }
}
