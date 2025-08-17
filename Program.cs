using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class BorkScanner
{
    //  Objects to help manage writing to console with multiple threads
    private static readonly object _consoleLock = new object();
    private static DateTime _lastProgressUpdate = DateTime.MinValue;
    private static readonly TimeSpan _progressInterval = TimeSpan.FromMilliseconds(500); // Update progress bar ~2x/sec

    static async Task Main(string[] args)
    {
        // Show usage if no args or --help flag provided
        if (args.Length < 1 || args.Contains("--help"))
        {
            PrintHelp();
            return;
        }

        // ARGUMENTS
        //  REQUIRED
        //      directory (filepath)
        //      scan mode (full/fast) default: full
        //  OPTIONAL
        //      --fileThreads (int) default: logical processors / 2
        //      --ffmpegInstances (int) default: 4
        //      --recursive (flag) default: true, can be disabled with --norecursive

        string directory = args[0];

        // Scan mode can be full (entire file) or fast (first frame only)
        string scanMode = "full";
        if (args.Length > 1 && (args[1].Equals("fast", StringComparison.OrdinalIgnoreCase) || args[1].Equals("full", StringComparison.OrdinalIgnoreCase)))
            scanMode = args[1].ToLower();

        // ffmpeg argument changes based on scan type
        string ffmpegScanArg = (scanMode == "fast") ? " -frames:v 1 " : " ";

        // Default concurrency limits
        int fileThreads = Environment.ProcessorCount / 2;   // Controls number of files processed at once
        int ffmpegInstances = 4;                           // Controls number of ffmpeg processes allowed
        bool recursive = true;                             // Controls directory traversal

        // Parse optional args
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--filethreads":   //  Sets the amount of files to be processed concurrently, if larger than logical processor count, set it to that
                    if (i + 1 < args.Length)
                    {
                        if (int.TryParse(args[i + 1], out int ft))
                        {
                            if (fileThreads > Environment.ProcessorCount)
                            {
                                fileThreads = Environment.ProcessorCount;
                            }
                            else
                            {
                                fileThreads = ft;
                            }
                            i++;
                        }

                        else
                        {
                            Console.WriteLine($"Warning: Invalid value for --filethreads: {args[i + 1]}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Warning: --filethreads flag requires a value");
                    }
                    break;

                case "--ffmpeginstances":
                    if (i + 1 < args.Length)
                    {
                        if (int.TryParse(args[i + 1], out int fi))
                        {
                            ffmpegInstances = fi;
                            i++;
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Invalid value for --ffmpeginstances: {args[i + 1]}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Warning: --ffmpeginstances flag requires a value");
                    }
                    break;

                case "--recursive":
                    recursive = true;
                    break;

                case "--norecursive":
                    recursive = false;
                    break;

                default:
                    if (args[i].StartsWith("--") && args[i].Contains("="))
                        Console.WriteLine($"Warning: Please separate flag and value: '{args[i]}' should be '--flag value'");
                    break;
            }
        }

        // File extensions supported for scanning
        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".webm"
        };

        var allExtensions = videoExtensions; // Easy expansion later if adding image checking

        // Enumerate files — wrap in try/catch for directory/permission issues
        List<string> allFiles;
        try
        {
            allFiles = Directory.EnumerateFiles(
                    directory,
                    "*.*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => allExtensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (Exception ex) when (
            ex is DirectoryNotFoundException ||
            ex is UnauthorizedAccessException ||
            ex is IOException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Could not access directory '{directory}'. {ex.Message}");
            Console.ResetColor();
            return;
        }

        int totalFiles = allFiles.Count;
        int processedFiles = 0;

        // Print scan info to console before starting scan
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"Performing a {scanMode} scan of directory: {Path.GetFullPath(directory)}");
        Console.WriteLine($"Using {fileThreads} parallel threads, max {ffmpegInstances} FFmpeg processes");
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Formats to scan: {string.Join(", ", allExtensions)}");
        Console.WriteLine($"Recursive: {recursive}");
        Console.WriteLine($"Total files found: {totalFiles}");
        Console.WriteLine($"FFmpeg Command: ffmpeg -v error -i <filename>{ffmpegScanArg}-f null -");
        Console.WriteLine();

        // Thread-safe collections for results
        var majorErrors = new ConcurrentBag<(string File, string Info)>();
        var minorErrors = new ConcurrentBag<(string File, string Info)>();
        var cleanFiles = new ConcurrentBag<string>();

        // Function to print a progress bar
        void UpdateProgressBar()
        {
            lock (_consoleLock)
            {
                int currentLine = Console.CursorTop;
                Console.SetCursorPosition(0, currentLine);
                int barWidth = 30;
                double percent = (double)processedFiles / totalFiles;
                int fill = (int)(percent * barWidth);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[");
                Console.Write(new string('█', fill));
                Console.Write(new string(' ', barWidth - fill));
                Console.Write("] ");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{percent * 100:0.0}% | ");

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"Major: {majorErrors.Count} | ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"Minor: {minorErrors.Count} | ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"Clean: {cleanFiles.Count}   ");

                Console.ResetColor();
            }
        }

        if (allFiles.Count > 0)
        {
            Console.WriteLine($"Scanning first file: {allFiles[0]}");
        }

        UpdateProgressBar();    // Print progress bar to screen before scanning first file, otherwise program looks like it's hanging while the first file processes

        // Semaphore is used to limit concurrent tasks
        using var semaphore = new SemaphoreSlim(fileThreads);
        using var ffmpegSemaphore = new SemaphoreSlim(ffmpegInstances);

        // Process each file
        var tasks = allFiles.Select(async file =>
        {
            // Create main semaphore task
            await semaphore.WaitAsync();
            try
            {
                bool major = false;
                bool minor = false;
                string errorInfo = "";

                string ext = Path.GetExtension(file);
                if (videoExtensions.Contains(ext))
                {
                    // Create sub semaphore for ffmpeg thread
                    await ffmpegSemaphore.WaitAsync();
                    try
                    {
                        var proc = new Process
                        {
                            StartInfo = new ProcessStartInfo(
                                "ffmpeg", $"-v error -i \"{file}\"{ffmpegScanArg}-f null -")
                            {
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        try
                        {
                            proc.Start();

                            // Set process priority lower than normal
                            // Windows only — Linux/macOS uses 'nice' externally
#if WINDOWS
                            proc.PriorityClass = ProcessPriorityClass.BelowNormal;
#endif
                        }
                        catch
                        {
                            // On Linux/macOS priority may fail, ignore
                        }

                        string errors = await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();

                        // Filters out common major errors and marks anything else as minor
                        if (!string.IsNullOrEmpty(errors))
                        {
                            major = errors.Contains("moov") || errors.Contains("could not");
                            minor = !major;
                            errorInfo = errors.Trim().Replace("\r\n", "; ");
                        }
                    }
                    finally
                    {
                        ffmpegSemaphore.Release();
                    }
                }

                // Add errors to file if any exist then update UI with progress
                if (major) majorErrors.Add((file, errorInfo));
                else if (minor) minorErrors.Add((file, errorInfo));
                else cleanFiles.Add(file);

                // Safely increment and update progress occasionally
                int processed = Interlocked.Increment(ref processedFiles);
                var now = DateTime.UtcNow;
                if ((now - _lastProgressUpdate) > _progressInterval || processed == allFiles.Count)
                {
                    lock (_consoleLock)
                    {
                        UpdateProgressBar();
                        _lastProgressUpdate = now;
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Console.WriteLine("\nScan complete!");

        // Output results
        string outputDir = Path.Combine(Environment.CurrentDirectory, "BorkScans");
        Directory.CreateDirectory(outputDir);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outputFile = Path.Combine(outputDir, $"BorkScan_{timestamp}.txt");

        using var writer = new StreamWriter(outputFile);
        writer.WriteLine("=== MAJOR ERRORS ===");
        foreach (var e in majorErrors)
            writer.WriteLine($"{e.File} | {e.Info}");

        writer.WriteLine("\n=== MINOR ERRORS ===");
        foreach (var e in minorErrors)
            writer.WriteLine($"{e.File} | {e.Info}");

        writer.WriteLine("\n=== CLEAN FILES ===");
        foreach (var f in cleanFiles)
            writer.WriteLine(f);

        Console.WriteLine($"Output written to: {outputFile}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("BorkScanner - Scan video files for corruption using ffmpeg");
        Console.WriteLine();
        Console.WriteLine("Usage: BorkScanner <directory> [full|fast] [--filethreads <int>] [--ffmpeginstances <int>] [--recursive|--norecursive]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <directory>           Directory to scan (required)");
        Console.WriteLine("  full|fast             Scan mode. 'full' = entire file, 'fast' = first frame only (default: full)");
        Console.WriteLine("  --filethreads <int>   Number of file-processing threads (default: logical processors / 2)");
        Console.WriteLine("  --ffmpeginstances <int> Max number of concurrent ffmpeg processes (default: 4)");
        Console.WriteLine("  --recursive           Scan subdirectories (default: true)");
        Console.WriteLine("  --norecursive         Disable scanning subdirectories");
    }
}
