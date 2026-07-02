using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

/// <summary>
/// Guarda en disco, dentro del propio servidor, tanto los documentos subidos por el usuario
/// como los documentos generados por el chatbot. Es deliberadamente independiente del
/// almacenamiento interno de AnythingLLM: aquí vive "la copia del servidor" que pide el enunciado.
/// </summary>
public class DocumentStorageService : IDocumentStorageService
{
    private readonly string _uploadsPath;
    private readonly string _generatedPath;

    public DocumentStorageService(IWebHostEnvironment environment)
    {
        var root = Path.Combine(environment.ContentRootPath, "Storage");
        _uploadsPath = Path.Combine(root, "Uploads");
        _generatedPath = Path.Combine(root, "Generated");

        Directory.CreateDirectory(_uploadsPath);
        Directory.CreateDirectory(_generatedPath);
    }

    public async Task<string> SaveUploadedFileAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var safeName = MakeUniqueSafeFileName(_uploadsPath, fileName);
        var destinationPath = Path.Combine(_uploadsPath, safeName);

        await using var fileStream = File.Create(destinationPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return safeName;
    }

    public async Task<string> SaveGeneratedDocumentAsync(string fileName, string markdownContent, CancellationToken cancellationToken)
    {
        var safeName = MakeUniqueSafeFileName(_generatedPath, EnsureExtension(fileName, ".md"));
        var destinationPath = Path.Combine(_generatedPath, safeName);

        await File.WriteAllTextAsync(destinationPath, markdownContent, cancellationToken);

        return safeName;
    }

    public IReadOnlyList<DocumentDto> ListDocuments(IReadOnlySet<string> embeddedFileNames)
    {
        var uploaded = EnumerateFolder(_uploadsPath, DocumentCategory.Uploaded, embeddedFileNames);
        var generated = EnumerateFolder(_generatedPath, DocumentCategory.Generated, embeddedFileNames);

        return uploaded.Concat(generated)
            .OrderByDescending(doc => doc.CreatedAtUtc)
            .ToList();
    }

    public string? ResolvePhysicalPath(string fileName)
    {
        var safeName = SanitizeFileName(fileName);

        foreach (var folder in new[] { _uploadsPath, _generatedPath })
        {
            var candidate = Path.Combine(folder, safeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<DocumentDto> EnumerateFolder(
        string folderPath, DocumentCategory category, IReadOnlySet<string> embeddedFileNames)
    {
        foreach (var filePath in Directory.EnumerateFiles(folderPath))
        {
            var info = new FileInfo(filePath);
            yield return new DocumentDto
            {
                FileName = info.Name,
                Category = category,
                SizeInBytes = info.Length,
                CreatedAtUtc = info.CreationTimeUtc,
                EmbeddedInWorkspace = embeddedFileNames.Contains(Path.GetFileNameWithoutExtension(info.Name))
                    || embeddedFileNames.Contains(info.Name)
            };
        }
    }

    private static string EnsureExtension(string? fileName, string extension)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? $"documento-{DateTime.UtcNow:yyyyMMddHHmmss}" : fileName;
        return Path.HasExtension(name) ? name : name + extension;
    }

    /// <summary>Quita cualquier componente de ruta e impide path traversal (../, rutas absolutas).</summary>
    private static string SanitizeFileName(string fileName)
    {
        var nameOnly = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(nameOnly))
        {
            throw new ArgumentException("Nombre de archivo inválido.", nameof(fileName));
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            nameOnly = nameOnly.Replace(invalidChar, '_');
        }

        return nameOnly;
    }

    private static string MakeUniqueSafeFileName(string folderPath, string fileName)
    {
        var safeName = SanitizeFileName(fileName);
        var extension = Path.GetExtension(safeName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);

        var candidate = safeName;
        var counter = 1;
        while (File.Exists(Path.Combine(folderPath, candidate)))
        {
            candidate = $"{baseName}-{counter}{extension}";
            counter++;
        }

        return candidate;
    }
}
