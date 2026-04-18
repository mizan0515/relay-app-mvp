namespace CodexClaudeRelay.Core.Runtime;

/// <summary>
/// Pure boundary logic for <see cref="Broker.RelayBroker.EvaluateRotationReason"/>.
/// Extracted so the G8 semantic half can be exercised headlessly by
/// <see cref="RotationSmokeRunner"/> without constructing the full broker.
/// </summary>
public static class RotationEvaluator
{
    public static string? Evaluate(
        int turnsSinceLastRotation,
        int maxTurnsPerSession,
        DateTimeOffset sessionStartedAt,
        TimeSpan maxSessionDuration,
        DateTimeOffset now)
    {
        if (turnsSinceLastRotation >= maxTurnsPerSession)
        {
            return $"Planned rotation triggered after {turnsSinceLastRotation} turns.";
        }

        var sessionAge = now - sessionStartedAt;
        if (sessionAge >= maxSessionDuration)
        {
            return $"Planned rotation triggered after {sessionAge:mm\\:ss}.";
        }

        return null;
    }
}
