using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChatbotDocs.Api.Models;
using ChatbotDocs.Api.Models.AnythingLlm;

namespace ChatbotDocs.Api.Services;

/// <summary>
/// Envuelve la Developer API de AnythingLLM (http://localhost:3001/api/docs).
/// AnythingLLM es quien habla directamente con Ollama para embeddings y generación;
/// este cliente solo orquesta workspaces, chat, subida de documentos y su vectorización.
/// Cada "carpeta/caso" del backend se mapea 1:1 a un workspace de AnythingLLM, para que
/// sus documentos nunca se mezclen con los de otro caso.
/// </summary>
public class AnythingLlmClient : IAnythingLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnythingLlmClient> _logger;

    public AnythingLlmClient(HttpClient httpClient, ILogger<AnythingLlmClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> EnsureWorkspaceAsync(string desiredSlug, CancellationToken cancellationToken)
    {
        using var listResponse = await _httpClient.GetAsync("/api/v1/workspaces", cancellationToken);
        await EnsureSuccessAsync(listResponse, "No se pudo listar los workspaces de AnythingLLM", cancellationToken);

        var list = await listResponse.Content.ReadFromJsonAsync<AnythingLlmWorkspacesListResponse>(cancellationToken: cancellationToken);
        var existing = list?.Workspaces?.FirstOrDefault(w => string.Equals(w.Slug, desiredSlug, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(existing?.Slug))
        {
            return existing.Slug;
        }

        var createBody = new AnythingLlmCreateWorkspaceRequestBody { Name = desiredSlug };
        using var createResponse = await _httpClient.PostAsJsonAsync("/api/v1/workspace/new", createBody, cancellationToken);
        await EnsureSuccessAsync(createResponse, "No se pudo crear el workspace en AnythingLLM", cancellationToken);

        var created = await createResponse.Content.ReadFromJsonAsync<AnythingLlmCreateWorkspaceResponse>(cancellationToken: cancellationToken);
        return created?.Workspace?.Slug
            ?? throw new AnythingLlmException("AnythingLLM no devolvió el slug del workspace creado.");
    }

    public async Task<ChatResponse> ChatAsync(string workspaceSlug, string message, string mode, string? threadSlug, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(threadSlug)
            ? $"/api/v1/workspace/{workspaceSlug}/chat"
            : $"/api/v1/workspace/{workspaceSlug}/thread/{threadSlug}/chat";

        var body = new AnythingLlmChatRequestBody { Message = message, Mode = mode };

        using var response = await _httpClient.PostAsJsonAsync(path, body, cancellationToken);
        await EnsureSuccessAsync(response, "No se pudo obtener respuesta del chat de AnythingLLM", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<AnythingLlmChatApiResponse>(cancellationToken: cancellationToken)
            ?? throw new AnythingLlmException("AnythingLLM devolvió una respuesta vacía.");

        if (!string.IsNullOrEmpty(result.Error))
        {
            throw new AnythingLlmException($"AnythingLLM devolvió un error: {result.Error}");
        }

        return new ChatResponse
        {
            Answer = result.TextResponse ?? string.Empty,
            Sources = (result.Sources ?? new List<AnythingLlmSource>())
                .Select(s => new ChatSourceDto { Title = s.Title ?? "documento", Excerpt = Truncate(s.Text) })
                .ToList()
        };
    }

    public async Task<string> UploadDocumentAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "file", fileName);

        using var response = await _httpClient.PostAsync("/api/v1/document/upload", content, cancellationToken);
        await EnsureSuccessAsync(response, "No se pudo subir el documento a AnythingLLM", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<AnythingLlmUploadApiResponse>(cancellationToken: cancellationToken);

        if (result is null || !result.Success || result.Documents is null || result.Documents.Count == 0)
        {
            throw new AnythingLlmException(result?.Error ?? "AnythingLLM no devolvió el documento subido.");
        }

        return result.Documents[0].Location
            ?? throw new AnythingLlmException("AnythingLLM no devolvió la ubicación del documento subido.");
    }

    public async Task EmbedDocumentsAsync(string workspaceSlug, IEnumerable<string> documentLocations, CancellationToken cancellationToken)
    {
        var body = new AnythingLlmUpdateEmbeddingsRequestBody { Adds = documentLocations.ToList() };

        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/v1/workspace/{workspaceSlug}/update-embeddings", body, cancellationToken);

        await EnsureSuccessAsync(response, "No se pudo embeber el documento en el workspace", cancellationToken);
    }

    public async Task PinDocumentAsync(string workspaceSlug, string documentLocation, CancellationToken cancellationToken)
    {
        var body = new AnythingLlmUpdatePinRequestBody { DocPath = documentLocation, PinStatus = true };

        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/v1/workspace/{workspaceSlug}/update-pin", body, cancellationToken);

        await EnsureSuccessAsync(response, "No se pudo fijar (pin) el documento en el workspace", cancellationToken);
    }

    public async Task<IReadOnlySet<string>> ListEmbeddedDocumentTitlesAsync(string workspaceSlug, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/workspace/{workspaceSlug}", cancellationToken);
        await EnsureSuccessAsync(response, "No se pudo consultar el workspace en AnythingLLM", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<AnythingLlmWorkspaceDetailsResponse>(cancellationToken: cancellationToken);

        var titles = result?.Workspace?
            .SelectMany(w => w.Documents ?? new List<AnythingLlmWorkspaceDocument>())
            .Select(doc => doc.Title ?? doc.Filename ?? string.Empty)
            .Where(title => !string.IsNullOrEmpty(title))
            ?? Enumerable.Empty<string>();

        return titles.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string errorPrefix, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError("AnythingLLM respondió {StatusCode}: {Body}", (int)response.StatusCode, body);
        throw new AnythingLlmException($"{errorPrefix} (HTTP {(int)response.StatusCode}).");
    }

    private static string Truncate(string? text, int maxLength = 280)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
