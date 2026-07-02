using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

public interface IAnythingLlmClient
{
    Task<ChatResponse> ChatAsync(string message, string mode, string? threadSlug, CancellationToken cancellationToken);

    /// <summary>Sube un archivo al almacén de documentos de AnythingLLM y devuelve su "location" (folder/archivo.json).</summary>
    Task<string> UploadDocumentAsync(Stream fileStream, string fileName, CancellationToken cancellationToken);

    /// <summary>Embebe (vectoriza) uno o más documentos ya subidos dentro del workspace configurado.</summary>
    Task EmbedDocumentsAsync(IEnumerable<string> documentLocations, CancellationToken cancellationToken);

    /// <summary>Lista los nombres de documentos ya conocidos por AnythingLLM (para saber cuáles están embebidos).</summary>
    Task<IReadOnlySet<string>> ListKnownDocumentTitlesAsync(CancellationToken cancellationToken);
}
