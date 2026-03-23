using KaiAssistant.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace KaiAssistant.Infrastructure.HostedServices;

public sealed class ResumeCacheWarmupHostedService : IHostedService
{
    private const string WarmupLockKey = "startup:warmup:resume:lock";

    private readonly IResumeRepository _resumeRepository;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<ResumeCacheWarmupHostedService> _logger;

    public ResumeCacheWarmupHostedService(IResumeRepository resumeRepository, IServiceProvider serviceProvider, ILogger<ResumeCacheWarmupHostedService> logger)
    {
        _resumeRepository = resumeRepository;
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_redis is not null)
            {
                var db = _redis.GetDatabase();
                var token = $"{Environment.MachineName}:{Guid.NewGuid():N}";
                var acquired = await db.StringSetAsync(WarmupLockKey, token, TimeSpan.FromMinutes(2), when: When.NotExists).ConfigureAwait(false);
                if (!acquired)
                {
                    _logger.LogInformation("Resume cache warmup skipped; lock is owned by another instance.");
                    return;
                }
            }

            await _resumeRepository.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Resume cache warmup completed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resume cache warmup failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
