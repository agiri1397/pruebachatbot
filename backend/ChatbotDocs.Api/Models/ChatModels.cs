namespace ChatbotDocs.Api.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// "query" responde solo si hay contexto en los documentos embebidos;
    /// "chat" permite que el modelo conteste también con conocimiento general.
    /// </summary>
    public string Mode { get; set; } = "query";

    /// <summary>
    /// Slug de hilo opcional para mantener conversaciones independientes.
    /// Si es null se usa el chat general del workspace.
    /// </summary>
    public string? ThreadSlug { get; set; }
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;

    public IReadOnlyList<ChatSourceDto> Sources { get; set; } = Array.Empty<ChatSourceDto>();
}

public class ChatSourceDto
{
    public string Title { get; set; } = string.Empty;

    public string Excerpt { get; set; } = string.Empty;
}
