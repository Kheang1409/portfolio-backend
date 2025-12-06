namespace KaiAssistant.Application.Interfaces;
public interface IGeminiGateway
{
    Task<(string? Body, string? UsedModel)> SendGenerationRequestAsync(string payloadJson, IEnumerable<string> models, CancellationToken cancellationToken = default);
}
