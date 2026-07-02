using ChatbotDocs.Api.Models;
using ChatbotDocs.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotDocs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IAnythingLlmClient _anythingLlmClient;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IAnythingLlmClient anythingLlmClient, ILogger<ChatController> logger)
    {
        _anythingLlmClient = anythingLlmClient;
        _logger = logger;
    }

    /// <summary>
    /// Envía un mensaje al workspace de AnythingLLM (que a su vez usa Ollama) y devuelve
    /// la respuesta junto con los fragmentos de los documentos usados como contexto.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("El mensaje no puede estar vacío.");
        }

        try
        {
            var response = await _anythingLlmClient.ChatAsync(
                request.Message, request.Mode, request.ThreadSlug, cancellationToken);

            return Ok(response);
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "Fallo al consultar a AnythingLLM");
            return Problem(title: "El servicio de chat no está disponible", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
