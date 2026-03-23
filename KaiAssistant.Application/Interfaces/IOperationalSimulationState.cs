namespace KaiAssistant.Application.Interfaces;

public interface IOperationalSimulationState
{
    DateTimeOffset? ForceAiThrottleUntilUtc { get; }
    int OutboxArtificialDelayMs { get; }

    void ForceAiThrottleFor(TimeSpan duration);
    void SetOutboxArtificialDelay(int delayMs);
}
