using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

public interface IDocumentStorageService
{
    Task<string> SaveUploadedFileAsync(string carpeta, Stream content, string fileName, CancellationToken cancellationToken);

    Task<string> SaveGeneratedDocumentAsync(string carpeta, string fileName, string markdownContent, CancellationToken cancellationToken);

    IReadOnlyList<DocumentDto> ListDocuments(string carpeta, IReadOnlySet<string> embeddedFileNames);

    /// <summary>Busca el archivo por nombre en ambas categorías dentro de la carpeta indicada, o null si no existe.</summary>
    string? ResolvePhysicalPath(string carpeta, string fileName);
}
