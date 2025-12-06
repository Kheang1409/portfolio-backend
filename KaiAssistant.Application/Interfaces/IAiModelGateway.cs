namespace KaiAssistant.Application.Interfaces;

public interface IAiModelGateway
{
    Task<(string? Body, string? UsedModel)> SendGenerationRequestAsync(string payloadJson, IEnumerable<string> models, CancellationToken cancellationToken = default);

    Task<string?> SendRequestAsync(string modelName, object payload, CancellationToken cancellationToken = default);
}
