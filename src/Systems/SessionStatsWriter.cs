using System.Text.Json;
using RhythmbulletPrototype.Models;

namespace RhythmbulletPrototype.Systems;

public sealed class SessionStatsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void Write(string outputPath, GameplaySessionStats stats)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(stats, JsonOptions);
        File.WriteAllText(outputPath, json);
    }
}
