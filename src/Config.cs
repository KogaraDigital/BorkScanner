using System;
using System.IO;
using System.Text.Json;

public class ScannerConfig
{
    public string[] VideoExtensions { get; set; } = { ".mp4", ".mkv", ".avi", ".mov", ".webm" };
    public int ProgressBarWidth { get; set; } = 30;
    public int ProgressUpdateIntervalMs { get; set; } = 250;
    public string DefaultScanMode { get; set; } = "fast";
    public int DefaultFileThreads { get; set; } = 4;
    public int DefaultFfmpegInstances { get; set; } = 2;
    public bool RecursiveByDefault { get; set; } = false;

    public static ScannerConfig Load()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scanner_config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<ScannerConfig>(json);
                return config ?? new ScannerConfig();
            }
            catch
            {
                return new ScannerConfig();
            }
        }
        return new ScannerConfig();
    }

    public void Save()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scanner_config.json");
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
    }
}
