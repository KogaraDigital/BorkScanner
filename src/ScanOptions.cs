using System;
using System.Collections.Generic;
using System.Linq;

public class ScanOptions
{
    // Directory to scan for video files
    public string? Directory { get; set; }
    // Scan mode: "full" or "fast"
    public string ScanMode { get; set; } = "full";
    // Number of file-processing threads
    public int FileThreads { get; set; } = Environment.ProcessorCount / 2;
    // Max number of concurrent ffmpeg processes
    public int FfmpegInstances { get; set; } = 4;
    // Whether to scan subdirectories
    public bool Recursive { get; set; } = true;

    // Parses command line arguments into a ScanOptions object
    public static ScanOptions Parse(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0])) return null;
        var options = new ScanOptions { Directory = args[0] };

        // Check for scan mode argument
        if (args.Length > 1 && (args[1].Equals("fast", StringComparison.OrdinalIgnoreCase) || args[1].Equals("full", StringComparison.OrdinalIgnoreCase)))
            options.ScanMode = args[1].ToLower();

        // Parse remaining arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--filethreads":
                    // Set number of file-processing threads
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int ft))
                    {
                        options.FileThreads = ft > Environment.ProcessorCount ? Environment.ProcessorCount : ft;
                        i++;
                    }
                    break;
                case "--ffmpeginstances":
                    // Set max number of ffmpeg processes
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int fi))
                    {
                        options.FfmpegInstances = fi;
                        i++;
                    }
                    break;
                case "--recursive":
                    // Enable recursive directory scan
                    options.Recursive = true;
                    break;
                case "--norecursive":
                    // Disable recursive directory scan
                    options.Recursive = false;
                    break;
                default:
                    // Warn about incorrect flag format
                    if (args[i].StartsWith("--") && args[i].Contains("="))
                        Console.WriteLine($"Warning: Please separate flag and value: '{args[i]}' should be '--flag value'");
                    break;
            }
        }
        return options;
    }
}
