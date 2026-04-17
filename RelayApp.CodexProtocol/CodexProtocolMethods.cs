namespace RelayApp.CodexProtocol;

public static class CodexProtocolMethods
{
    public const string Initialize = "initialize";
    public const string GetAuthStatus = "getAuthStatus";
    public const string ThreadStart = "thread/start";
    public const string TurnStart = "turn/start";

    public const string ThreadStartedNotification = "thread/started";
    public const string ThreadStatusChangedNotification = "thread/status/changed";
    public const string TurnStartedNotification = "turn/started";
    public const string TurnCompletedNotification = "turn/completed";
    public const string ItemStartedNotification = "item/started";
    public const string ItemCompletedNotification = "item/completed";
    public const string ItemAgentMessageDeltaNotification = "item/agentMessage/delta";
    public const string ThreadTokenUsageUpdatedNotification = "thread/tokenUsage/updated";
}
