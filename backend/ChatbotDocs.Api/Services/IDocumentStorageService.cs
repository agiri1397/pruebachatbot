using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

public interface IDocumentStorageService
{
    Task<string> SaveUploadedFileAsync(Stream content, string fileName, CancellationToken cancellationToken);

    Task<string> SaveGeneratedDocumentAsync(string fileName, string markdownContent, CancellationToken cancellationToken);

    IReadOnlyList<DocumentDto> ListDocuments(IReadOnlySet<string> embeddedFileNames);

    /// <summary>Busca el archivo por nombre en ambas categorías y devuelve su ruta física, o null si no existe.</summary>
    string? ResolvePhysicalPath(string fileName);
}
