using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

public interface IDocumentSyncService
{
    /// <summary>Nombres de las subcarpetas (casos) que existen dentro de la carpeta fija de documentos.</summary>
    IReadOnlyList<string> ListAvailableCarpetas();

    /// <summary>
    /// Escanea Documentos/{carpeta} y embebe + fija ("pin") en el workspace de ese caso los
    /// archivos que todavía no estén indexados, para que el chat pueda razonar sobre su
    /// contenido completo.
    /// </summary>
    Task<DocumentSyncResponse> SyncAsync(string carpeta, CancellationToken cancellationToken);

    IReadOnlyList<DocumentDto> ListSourceDocuments(string carpeta, IReadOnlySet<string> embeddedFileNames);
}
