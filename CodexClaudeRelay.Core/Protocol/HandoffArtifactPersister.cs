using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public static class HandoffArtifactPersister
{
    public static async Task<long> WriteAsync(TurnPacket packet, string outPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var body = HandoffArtifactWriter.Render(packet);

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = outPath + ".tmp";
        await File.WriteAllTextAsync(tmp, body, ct).ConfigureAwait(false);
        File.Move(tmp, outPath, overwrite: true);

        return new FileInfo(outPath).Length;
    }
}
