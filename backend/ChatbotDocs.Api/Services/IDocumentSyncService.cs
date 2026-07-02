using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

public interface IDocumentSyncService
{
    /// <summary>
    /// Escanea la carpeta fija de documentos del servidor y embebe + fija ("pin") en AnythingLLM
    /// los que todavía no estén indexados, para que el chat pueda razonar sobre su contenido completo.
    /// </summary>
    Task<DocumentSyncResponse> SyncAsync(CancellationToken cancellationToken);

    IReadOnlyList<DocumentDto> ListSourceDocuments(IReadOnlySet<string> embeddedFileNames);
}
