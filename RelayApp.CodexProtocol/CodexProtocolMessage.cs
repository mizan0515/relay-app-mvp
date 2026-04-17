using System.Text.Json;

namespace RelayApp.CodexProtocol;

public enum CodexProtocolMessageKind
{
    StdoutLine,
    StderrLine,
    Notification,
    ServerRequest,
}

public sealed record CodexProtocolMessage(
    CodexProtocolMessageKind Kind,
    string? Text = null,
    string? Method = null,
    JsonElement Payload = default);
