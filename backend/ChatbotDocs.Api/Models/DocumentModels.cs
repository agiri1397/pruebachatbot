namespace ChatbotDocs.Api.Models;

public enum DocumentCategory
{
    Uploaded,
    Generated,

    /// <summary>Documento colocado manualmente en la carpeta fija del servidor (ver DocumentSync).</summary>
    SourceFolder
}

public class DocumentDto
{
    public string FileName { get; set; } = string.Empty;

    public DocumentCategory Category { get; set; }

    public long SizeInBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool EmbeddedInWorkspace { get; set; }
}

public class UploadDocumentResponse
{
    public string FileName { get; set; } = string.Empty;

    public bool EmbeddedInWorkspace { get; set; }

    public string Message { get; set; } = string.Empty;
}

public class GenerateDocumentRequest
{
    /// <summary>Instrucción de qué documento generar, p.ej. "Resumen ejecutivo de las políticas de vacaciones".</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Nombre de archivo sin extensión; se generará como Markdown.</summary>
    public string? FileName { get; set; }

    /// <summary>Si es true, el documento generado se sube y embebe de nuevo en el workspace para poder consultarlo luego.</summary>
    public bool EmbedAfterGeneration { get; set; } = true;
}

public class GenerateDocumentResponse
{
    public string FileName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public bool EmbeddedInWorkspace { get; set; }
}

/// <summary>Resultado de sincronizar la carpeta fija de documentos del servidor con el workspace de AnythingLLM.</summary>
public class DocumentSyncResponse
{
    public IReadOnlyList<string> NewlyEmbedded { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> AlreadyEmbedded { get; set; } = Array.Empty<string>();

    public IReadOnlyList<DocumentSyncErrorDto> Errors { get; set; } = Array.Empty<DocumentSyncErrorDto>();
}

public class DocumentSyncErrorDto
{
    public string FileName { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;
}
