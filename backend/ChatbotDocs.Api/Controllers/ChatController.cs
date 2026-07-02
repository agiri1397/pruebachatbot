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
    /// Envía un mensaje al caso/carpeta indicado. Cada carpeta vive en su propio workspace de
    /// AnythingLLM (creado automáticamente la primera vez que se usa), aislado del resto de los
    /// casos, y responde usando el contexto de los documentos de esa carpeta.
    /// </summary>
    [HttpPost("{carpeta}")]
    public async Task<ActionResult<ChatResponse>> Chat(string carpeta, [FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("El mensaje no puede estar vacío.");
        }

        try
        {
            var workspaceSlug = await _anythingLlmClient.EnsureWorkspaceAsync(SlugHelper.Sanitize(carpeta), cancellationToken);
            var response = await _anythingLlmClient.ChatAsync(workspaceSlug, request.Message, request.Mode, request.ThreadSlug, cancellationToken);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AnythingLlmException ex)
        {
            _logger.LogError(ex, "Fallo al consultar a AnythingLLM para el caso {Carpeta}", carpeta);
            return Problem(title: "El servicio de chat no está disponible", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
