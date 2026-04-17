namespace RelayApp.Core.Broker;

public static class RelayBrokerOptionsValidator
{
    public static RelayBrokerOptions ClampAndReport(
        RelayBrokerOptions input,
        out List<string> warnings)
    {
        warnings = [];
        var options = input;

        if (options.PerTurnTimeout <= TimeSpan.Zero)
        {
            warnings.Add($"PerTurnTimeout must be > 0; using 00:05:00 instead of {options.PerTurnTimeout:c}.");
            options = options with { PerTurnTimeout = TimeSpan.FromMinutes(5) };
        }

        if (options.MaxCumulativeOutputTokens <= 0)
        {
            warnings.Add($"MaxCumulativeOutputTokens must be > 0; using 30000 instead of {options.MaxCumulativeOutputTokens}.");
            options = options with { MaxCumulativeOutputTokens = 30000 };
        }

        if (options.MaxRepairAttempts < 1)
        {
            warnings.Add($"MaxRepairAttempts must be >= 1; using 2 instead of {options.MaxRepairAttempts}.");
            options = options with { MaxRepairAttempts = 2 };
        }

        if (options.MaxSessionDuration <= TimeSpan.Zero)
        {
            warnings.Add($"MaxSessionDuration must be > 0; using 00:15:00 instead of {options.MaxSessionDuration:c}.");
            options = options with { MaxSessionDuration = TimeSpan.FromMinutes(15) };
        }

        if (options.MaxTurnsPerSession < 1)
        {
            warnings.Add($"MaxTurnsPerSession must be >= 1; using 20 instead of {options.MaxTurnsPerSession}.");
            options = options with { MaxTurnsPerSession = 20 };
        }

        if (options.CacheReadRatioFloor < 0 || options.CacheReadRatioFloor > 1)
        {
            warnings.Add($"CacheReadRatioFloor must be between 0 and 1; using 0.30 instead of {options.CacheReadRatioFloor:F2}.");
            options = options with { CacheReadRatioFloor = 0.30d };
        }

        if (options.ConsecutiveLowCacheTurnsThreshold < 1)
        {
            warnings.Add($"ConsecutiveLowCacheTurnsThreshold must be >= 1; using 3 instead of {options.ConsecutiveLowCacheTurnsThreshold}.");
            options = options with { ConsecutiveLowCacheTurnsThreshold = 3 };
        }

        if (options.FallbackClaudeBudgetUsd <= 0)
        {
            warnings.Add($"FallbackClaudeBudgetUsd must be > 0; using 0.50 instead of {options.FallbackClaudeBudgetUsd:F2}.");
            options = options with { FallbackClaudeBudgetUsd = 0.50d };
        }

        if (options.MaxClaudeCostUsd.HasValue)
        {
            if (options.MaxClaudeCostUsd.Value <= 0)
            {
                warnings.Add($"MaxClaudeCostUsd must be > 0 when set; disabling instead of {options.MaxClaudeCostUsd.Value:F2}.");
                options = options with { MaxClaudeCostUsd = null };
            }
            else if (options.MaxClaudeCostUsd.Value > 1000d)
            {
                warnings.Add($"MaxClaudeCostUsd must be <= 1000.00; using 1000.00 instead of {options.MaxClaudeCostUsd.Value:F2}.");
                options = options with { MaxClaudeCostUsd = 1000d };
            }
        }

        var jobObject = options.JobObject;
        if (jobObject.UserCpuTimePerJob <= TimeSpan.Zero)
        {
            warnings.Add($"JobObject.UserCpuTimePerJob must be > 0; using 00:10:00 instead of {jobObject.UserCpuTimePerJob:c}.");
            jobObject = jobObject with { UserCpuTimePerJob = TimeSpan.FromMinutes(10) };
        }

        if (jobObject.ActiveProcessLimit < 2)
        {
            warnings.Add($"JobObject.ActiveProcessLimit must be >= 2; using 32 instead of {jobObject.ActiveProcessLimit}.");
            jobObject = jobObject with { ActiveProcessLimit = 32 };
        }

        if (jobObject.JobMemoryLimitBytes < 64L * 1024 * 1024)
        {
            warnings.Add($"JobObject.JobMemoryLimitBytes must be >= 67108864; using 4294967296 instead of {jobObject.JobMemoryLimitBytes}.");
            jobObject = jobObject with { JobMemoryLimitBytes = 4L * 1024 * 1024 * 1024 };
        }

        if (jobObject != options.JobObject)
        {
            options = options with { JobObject = jobObject };
        }

        return options;
    }
}
