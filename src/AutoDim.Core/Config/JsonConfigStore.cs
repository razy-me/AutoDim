using System.Text;
using System.Text.Json;

namespace AutoDim.Core.Config;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string ConfigPath { get; }

    public JsonConfigStore(string? configPath = null)
    {
        ConfigPath = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoDim",
                "config.json")
            : configPath;
    }

    public DimConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return DimConfig.Default;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<DimConfig>(json, SerializerOptions);
            if (parsed is null)
            {
                BackupCorruptFile();
                return DimConfig.Default;
            }

            return parsed.Validated();
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return DimConfig.Default;
        }
    }

    public void Save(DimConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        DimConfig validated = config.Validated();

        string? directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{ConfigPath}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(validated, SerializerOptions);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, ConfigPath, overwrite: true);
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
                // Best-effort temp cleanup
            }

            throw;
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string backupPath = $"{ConfigPath}.corrupt.{timestamp}.bak";
            File.Copy(ConfigPath, backupPath, overwrite: true);
        }
        catch
        {
            // Best-effort backup
        }
    }
}
