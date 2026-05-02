using KaiAssistant.Application.AI;
namespace KaiAssistant.Application.Interfaces;
public interface ISemanticCacheService
{
    Task<AiResponse?> TryGetAsync(string prompt, CancellationToken cancellationToken = default);
    Task SetAsync(string prompt, AiResponse response, CancellationToken cancellationToken = default);
}