using ChatbotDocs.Api.Models;
using ChatbotDocs.Api.Options;
using Microsoft.Extensions.Options;

namespace ChatbotDocs.Api.Services;

/// <summary>
/// Sincroniza las subcarpetas de una carpeta fija del servidor (Documentos/{carpeta}), donde un
/// administrador coloca manualmente documentos de un caso (marco legal, formularios, etc.), con
/// el workspace de AnythingLLM de ese mismo caso (uno por carpeta, ver <see cref="IAnythingLlmClient.EnsureWorkspaceAsync"/>).
/// Cada documento nuevo se embebe (queda buscable) y además se "pinea", para que el chat de ese
/// caso pueda responder preguntas que requieren considerar el contenido completo de varios
/// documentos a la vez (p. ej. cruzar el marco legal con los formularios ingresados).
/// </summary>
public class DocumentSyncService : IDocumentSyncService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".docx", ".csv", ".json"
    };

    private readonly string _rootFolderPath;
    private readonly IAnythingLlmClient _anythingLlmClient;
    private readonly ILogger<DocumentSyncService> _logger;

    public DocumentSyncService(
        IWebHostEnvironment environment,
        IOptions<DocumentSyncOptions> options,
        IAnythingLlmClient anythingLlmClient,
        ILogger<DocumentSyncService> logger)
    {
        _rootFolderPath = Path.Combine(environment.ContentRootPath, options.Value.FolderPath);
        Directory.CreateDirectory(_rootFolderPath);
        _anythingLlmClient = anythingLlmClient;
        _logger = logger;
    }

    public IReadOnlyList<string> ListAvailableCarpetas() =>
        Directory.EnumerateDirectories(_rootFolderPath)
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<DocumentSyncResponse> SyncAsync(string carpeta, CancellationToken cancellationToken)
    {
        var carpetaSlug = SlugHelper.Sanitize(carpeta);
        var workspaceSlug = await _anythingLlmClient.EnsureWorkspaceAsync(carpetaSlug, cancellationToken);
        var knownTitles = await _anythingLlmClient.ListEmbeddedDocumentTitlesAsync(workspaceSlug, cancellationToken);

        var newlyEmbedded = new List<string>();
        var alreadyEmbedded = new List<string>();
        var errors = new List<DocumentSyncErrorDto>();

        foreach (var filePath in EnumerateSupportedFiles(carpetaSlug))
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

                await _anythingLlmClient.EmbedDocumentsAsync(workspaceSlug, new[] { location }, cancellationToken);

                try
                {
                    await _anythingLlmClient.PinDocumentAsync(workspaceSlug, location, cancellationToken);
                }
                catch (AnythingLlmException pinEx)
                {
                    _logger.LogWarning(pinEx,
                        "El documento {FileName} (caso {Carpeta}) se embebió pero no se pudo fijar (pin) " +
                        "automáticamente. Si tu versión de AnythingLLM no soporta este endpoint, fíjalo " +
                        "manualmente desde su interfaz para que el chat lo considere completo en cada respuesta.",
                        fileName, carpetaSlug);
                }

                newlyEmbedded.Add(fileName);
            }
            catch (AnythingLlmException ex)
            {
                _logger.LogError(ex, "No se pudo sincronizar el documento {FileName} del caso {Carpeta}", fileName, carpetaSlug);
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

    public IReadOnlyList<DocumentDto> ListSourceDocuments(string carpeta, IReadOnlySet<string> embeddedFileNames)
    {
        var carpetaSlug = SlugHelper.Sanitize(carpeta);
        var documents = new List<DocumentDto>();

        foreach (var filePath in EnumerateSupportedFiles(carpetaSlug))
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

    /// <summary>
    /// Busca, sin distinguir mayúsculas, la subcarpeta cuyo nombre coincide con el slug del caso.
    /// Si todavía no existe físicamente (el caso solo tiene documentos subidos por API, por
    /// ejemplo), no hay nada que sincronizar y se trata como una lista vacía en vez de un error.
    /// </summary>
    private IEnumerable<string> EnumerateSupportedFiles(string carpetaSlug)
    {
        var folderPath = Directory.EnumerateDirectories(_rootFolderPath)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), carpetaSlug, StringComparison.OrdinalIgnoreCase));

        if (folderPath is null)
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)));
    }
}
