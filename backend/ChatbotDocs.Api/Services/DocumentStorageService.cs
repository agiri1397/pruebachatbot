using ChatbotDocs.Api.Models;

namespace ChatbotDocs.Api.Services;

/// <summary>
/// Guarda en disco, dentro del propio servidor, tanto los documentos subidos por el usuario
/// como los documentos generados por el chatbot. Cada carpeta/caso tiene su propia
/// subcarpeta (Storage/Uploads/{carpeta} y Storage/Generated/{carpeta}), en paralelo a su
/// propio workspace en AnythingLLM, para que los archivos de un caso nunca se mezclen con
/// los de otro.
/// </summary>
public class DocumentStorageService : IDocumentStorageService
{
    private readonly string _uploadsRoot;
    private readonly string _generatedRoot;

    public DocumentStorageService(IWebHostEnvironment environment)
    {
        var root = Path.Combine(environment.ContentRootPath, "Storage");
        _uploadsRoot = Path.Combine(root, "Uploads");
        _generatedRoot = Path.Combine(root, "Generated");

        Directory.CreateDirectory(_uploadsRoot);
        Directory.CreateDirectory(_generatedRoot);
    }

    public async Task<string> SaveUploadedFileAsync(string carpeta, Stream content, string fileName, CancellationToken cancellationToken)
    {
        var folder = GetCarpetaPath(_uploadsRoot, carpeta);
        var safeName = MakeUniqueSafeFileName(folder, fileName);
        var destinationPath = Path.Combine(folder, safeName);

        await using var fileStream = File.Create(destinationPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return safeName;
    }

    public async Task<string> SaveGeneratedDocumentAsync(string carpeta, string fileName, string markdownContent, CancellationToken cancellationToken)
    {
        var folder = GetCarpetaPath(_generatedRoot, carpeta);
        var safeName = MakeUniqueSafeFileName(folder, EnsureExtension(fileName, ".md"));
        var destinationPath = Path.Combine(folder, safeName);

        await File.WriteAllTextAsync(destinationPath, markdownContent, cancellationToken);

        return safeName;
    }

    public IReadOnlyList<DocumentDto> ListDocuments(string carpeta, IReadOnlySet<string> embeddedFileNames)
    {
        var uploaded = EnumerateFolder(GetCarpetaPath(_uploadsRoot, carpeta), DocumentCategory.Uploaded, embeddedFileNames);
        var generated = EnumerateFolder(GetCarpetaPath(_generatedRoot, carpeta), DocumentCategory.Generated, embeddedFileNames);

        return uploaded.Concat(generated)
            .OrderByDescending(doc => doc.CreatedAtUtc)
            .ToList();
    }

    public string? ResolvePhysicalPath(string carpeta, string fileName)
    {
        var safeName = SanitizeFileName(fileName);

        foreach (var folder in new[] { GetCarpetaPath(_uploadsRoot, carpeta), GetCarpetaPath(_generatedRoot, carpeta) })
        {
            var candidate = Path.Combine(folder, safeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetCarpetaPath(string root, string carpeta)
    {
        var path = Path.Combine(root, SlugHelper.Sanitize(carpeta));
        Directory.CreateDirectory(path);
        return path;
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
