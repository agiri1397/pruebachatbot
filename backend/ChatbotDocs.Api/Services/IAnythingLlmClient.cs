using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

public interface IAnythingLlmClient
{
    /// <summary>Garantiza que exista un workspace para el slug indicado (lo crea si falta) y devuelve el slug real usado por AnythingLLM.</summary>
    Task<string> EnsureWorkspaceAsync(string desiredSlug, CancellationToken cancellationToken);

    Task<ChatResponse> ChatAsync(string workspaceSlug, string message, string mode, string? threadSlug, CancellationToken cancellationToken);

    /// <summary>Sube un archivo al almacén de documentos de AnythingLLM y devuelve su "location" (folder/archivo.json).</summary>
    Task<string> UploadDocumentAsync(Stream fileStream, string fileName, CancellationToken cancellationToken);

    /// <summary>Embebe (vectoriza) uno o más documentos ya subidos dentro del workspace indicado.</summary>
    Task EmbedDocumentsAsync(string workspaceSlug, IEnumerable<string> documentLocations, CancellationToken cancellationToken);

    /// <summary>
    /// Fija ("pin") un documento en el workspace indicado para que su contenido completo se incluya
    /// siempre como contexto, sin depender de la búsqueda semántica por fragmentos.
    /// </summary>
    Task PinDocumentAsync(string workspaceSlug, string documentLocation, CancellationToken cancellationToken);

    /// <summary>Lista los títulos de los documentos ya embebidos específicamente en el workspace indicado.</summary>
    Task<IReadOnlySet<string>> ListEmbeddedDocumentTitlesAsync(string workspaceSlug, CancellationToken cancellationToken);
}
