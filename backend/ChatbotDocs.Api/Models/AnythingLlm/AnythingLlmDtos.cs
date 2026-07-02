using System.Text.Json.Serialization;

namespace ChatbotDocs.Api.Models.AnythingLlm;

/// <summary>
/// Modelos que reflejan el contrato JSON de la Developer API de AnythingLLM
/// (ver http://&lt;host&gt;:3001/api/docs cuando la instancia está corriendo).
/// No se exponen fuera de <see cref="Services.AnythingLlmClient"/>.
/// </summary>
internal sealed class AnythingLlmChatApiResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("textResponse")]
    public string? TextResponse { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("sources")]
    public List<AnythingLlmSource>? Sources { get; set; }
}

internal sealed class AnythingLlmSource
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class AnythingLlmChatRequestBody
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "query";
}

internal sealed class AnythingLlmUploadApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("documents")]
    public List<AnythingLlmUploadedDocument>? Documents { get; set; }
}

internal sealed class AnythingLlmUploadedDocument
{
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

internal sealed class AnythingLlmUpdateEmbeddingsRequestBody
{
    [JsonPropertyName("adds")]
    public List<string> Adds { get; set; } = new();

    [JsonPropertyName("deletes")]
    public List<string> Deletes { get; set; } = new();
}

/// <summary>
/// "Pinear" un documento hace que AnythingLLM incluya su contenido COMPLETO en cada
/// consulta del workspace, en vez de solo los fragmentos más parecidos a la pregunta
/// (búsqueda semántica). Es lo que permite responder preguntas que requieren razonar
/// sobre varios documentos completos a la vez (p. ej. cruzar marco legal + formularios).
/// </summary>
internal sealed class AnythingLlmUpdatePinRequestBody
{
    [JsonPropertyName("docPath")]
    public string DocPath { get; set; } = string.Empty;

    [JsonPropertyName("pinStatus")]
    public bool PinStatus { get; set; }
}

internal sealed class AnythingLlmDocumentsApiResponse
{
    [JsonPropertyName("localFiles")]
    public AnythingLlmLocalFilesNode? LocalFiles { get; set; }
}

internal sealed class AnythingLlmLocalFilesNode
{
    [JsonPropertyName("items")]
    public List<AnythingLlmFolderNode>? Items { get; set; }
}

internal sealed class AnythingLlmFolderNode
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("items")]
    public List<AnythingLlmDocumentNode>? Items { get; set; }
}

internal sealed class AnythingLlmDocumentNode
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
