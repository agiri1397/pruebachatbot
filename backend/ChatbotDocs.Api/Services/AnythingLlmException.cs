namespace ChatbotDocs.Api.Services;

/// <summary>Se lanza cuando AnythingLLM responde con error o no está disponible.</summary>
public class AnythingLlmException : Exception
{
    public AnythingLlmException(string message) : base(message)
    {
    }

    public AnythingLlmException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
