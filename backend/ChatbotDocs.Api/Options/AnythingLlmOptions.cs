namespace ChatbotDocs.Api.Options;

public class AnythingLlmOptions
{
    public const string SectionName = "AnythingLlm";

    public string BaseUrl { get; set; } = "http://localhost:3001";

    public string ApiKey { get; set; } = string.Empty;

    public string WorkspaceSlug { get; set; } = string.Empty;

    public string DocumentFolder { get; set; } = "custom-documents";
}
