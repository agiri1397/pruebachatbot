using ChatbotDocs.Api.Models;
using ChatbotDocs.Api.Options;
using Microsoft.Extensions.Options;

namespace ChatbotDocs.Api.Services;

/// <summary>
/// Sincroniza una carpeta fija del servidor (donde un administrador coloca manualmente
/// documentos: marco legal, formularios, etc.) con un único workspace de AnythingLLM.
/// Cada documento nuevo se embebe (queda buscable) y además se "pinea", para que el chat
/// pueda responder preguntas que requieren considerar el contenido completo de varios
/// documentos a la vez (p. ej. cruzar el marco legal con los formularios ingresados).
/// </summary>
public class DocumentSyncService : IDocumentSyncService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".docx", ".csv", ".json"
    };

    private readonly string _folderPath;
    private readonly IAnythingLlmClient _anythingLlmClient;
    private readonly ILogger<DocumentSyncService> _logger;

    public DocumentSyncService(
        IWebHostEnvironment environment,
        IOptions<DocumentSyncOptions> options,
        IAnythingLlmClient anythingLlmClient,
        ILogger<DocumentSyncService> logger)
    {
        _folderPath = Path.Combine(environment.ContentRootPath, options.Value.FolderPath);
        Directory.CreateDirectory(_folderPath);
        _anythingLlmClient = anythingLlmClient;
        _logger = logger;
    }

    public async Task<DocumentSyncResponse> SyncAsync(CancellationToken cancellationToken)
    {
        var knownTitles = await _anythingLlmClient.ListKnownDocumentTitlesAsync(cancellationToken);

        var newlyEmbedded = new List<string>();
        var alreadyEmbedded = new List<string>();
        var errors = new List<DocumentSyncErrorDto>();

        foreach (var filePath in EnumerateSupportedFiles())
        {
            var fileName = Path.GetFileName(filePath);
            var titleWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            if (knownTitles.Contains(titleWithoutExtension) || knownTitles.Contains(fileName))
            {
                alreadyEmbedded.Add(fileName);
                continue;
            }

            try
            {
                string location;
                await using (var stream = File.OpenRead(filePath))
                {
                    location = await _anythingLlmClient.UploadDocumentAsync(stream, fileName, cancellationToken);
                }

                await _anythingLlmClient.EmbedDocumentsAsync(new[] { location }, cancellationToken);

                try
                {
                    await _anythingLlmClient.PinDocumentAsync(location, cancellationToken);
                }
                catch (AnythingLlmException pinEx)
                {
                    _logger.LogWarning(pinEx,
                        "El documento {FileName} se embebió pero no se pudo fijar (pin) automáticamente. " +
                        "Si tu versión de AnythingLLM no soporta este endpoint, fíjalo manualmente desde su interfaz " +
                        "para que el chat lo considere completo en cada respuesta.", fileName);
                }

                newlyEmbedded.Add(fileName);
            }
            catch (AnythingLlmException ex)
            {
                _logger.LogError(ex, "No se pudo sincronizar el documento {FileName}", fileName);
                errors.Add(new DocumentSyncErrorDto { FileName = fileName, Error = ex.Message });
            }
        }

        return new DocumentSyncResponse
        {
            NewlyEmbedded = newlyEmbedded,
            AlreadyEmbedded = alreadyEmbedded,
            Errors = errors
        };
    }

    public IReadOnlyList<DocumentDto> ListSourceDocuments(IReadOnlySet<string> embeddedFileNames)
    {
        var documents = new List<DocumentDto>();

        foreach (var filePath in EnumerateSupportedFiles())
        {
            var info = new FileInfo(filePath);
            documents.Add(new DocumentDto
            {
                FileName = info.Name,
                Category = DocumentCategory.SourceFolder,
                SizeInBytes = info.Length,
                CreatedAtUtc = info.CreationTimeUtc,
                EmbeddedInWorkspace = embeddedFileNames.Contains(Path.GetFileNameWithoutExtension(info.Name))
                    || embeddedFileNames.Contains(info.Name)
            });
        }

        return documents;
    }

    private IEnumerable<string> EnumerateSupportedFiles() =>
        Directory.EnumerateFiles(_folderPath, "*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)));
}
