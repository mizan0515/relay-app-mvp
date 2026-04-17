using System.Text.Json;
using System.Text.Json.Serialization;
using RelayApp.Core.Models;

namespace RelayApp.Core.Persistence;

public sealed class JsonRelaySessionStore : IRelaySessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _stateFilePath;

    public JsonRelaySessionStore(string stateFilePath)
    {
        _stateFilePath = stateFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
    }

    public async Task SaveAsync(RelaySessionState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_stateFilePath)!;
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(_stateFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_stateFilePath))
            {
                File.Replace(tempPath, _stateFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _stateFilePath);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup for abandoned temp files.
            }

            throw;
        }
    }

    public async Task<RelaySessionState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_stateFilePath);
        return await JsonSerializer.DeserializeAsync<RelaySessionState>(stream, SerializerOptions, cancellationToken);
    }
}
