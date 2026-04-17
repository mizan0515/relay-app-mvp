using System.Text.Json;

namespace RelayApp.CodexProtocol;

public sealed record CodexProtocolServerRequest(
    string Method,
    JsonElement Payload);

public sealed record CodexProtocolServerRequestResponse(
    bool Handled,
    object? Result)
{
    public static CodexProtocolServerRequestResponse FromResult(object? result) => new(true, result);

    public static CodexProtocolServerRequestResponse Unhandled() => new(false, null);
}
