namespace RelayApp.Core.Broker;

public sealed record RelayBrokerOptions
{
    public long MaxCumulativeOutputTokens { get; init; } = 30000;

    public double CacheReadRatioFloor { get; init; } = 0.30d;

    public int ConsecutiveLowCacheTurnsThreshold { get; init; } = 3;

    public int MaxTurnsPerSession { get; init; } = 20;

    public TimeSpan MaxSessionDuration { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxRepairAttempts { get; init; } = 2;

    public double FallbackClaudeBudgetUsd { get; init; } = 0.50d;

    public double? MaxClaudeCostUsd { get; init; }

    public TimeSpan PerTurnTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public RelayJobObjectOptions JobObject { get; init; } = new();
}

public sealed record RelayJobObjectOptions
{
    public TimeSpan UserCpuTimePerJob { get; init; } = TimeSpan.FromMinutes(10);

    public int ActiveProcessLimit { get; init; } = 32;

    public long JobMemoryLimitBytes { get; init; } = 4L * 1024 * 1024 * 1024;
}
