namespace ChatbotDocs.Api.Models;

public enum DocumentCategory
{
    Uploaded,
    Generated
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
