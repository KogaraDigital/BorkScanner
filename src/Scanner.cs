using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class Scanner
{
    // Static cache for error patterns loaded from external files
    private static List<string>? _majorPatterns = null;
    private static List<string>? _minorPatterns = null;

    // Helper to load patterns from a file
    private static List<string> LoadPatterns(string fileName)
    {
    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", fileName);
        if (File.Exists(path))
            return File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim().ToLowerInvariant()).ToList();
        return new List<string>();
    }
    // Options for the scan (directory, threads, etc.)
    private readonly ScanOptions _options;
    // Lock for thread-safe console updates
    private static readonly object _consoleLock = new object();
    // Last time the progress bar was updated
    private static DateTime _lastProgressUpdate = DateTime.MinValue;
    // How often to update the progress bar
    private static readonly TimeSpan _progressInterval = TimeSpan.FromMilliseconds(250);
    // Top line of the progress bar in the console
    private int progressBarTop = -1;
    // Number of lines used for progress bar and file display
    private int displayLines = 0;

    public Scanner(ScanOptions options)
    {
    // Store scan options
    _options = options;
    }

    public async Task RunAsync()
    {
    // Supported video file extensions
    var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mkv", ".avi", ".mov", ".webm" };

        // Find all video files in the target directory
        List<string> allFiles;
        try
        {
            if (string.IsNullOrWhiteSpace(_options.Directory))
                throw new ArgumentException("Scan directory is null or empty.");
            allFiles = Directory.EnumerateFiles(
                _options.Directory,
                "*.*",
                _options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => videoExtensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (Exception ex)
        {
            // Print error if directory can't be accessed
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Could not access directory '{_options.Directory}'. {ex.Message}");
            Console.ResetColor();
            return;
        }

    // Total number of files and processed count
    int totalFiles = allFiles.Count;
    int processedFiles = 0;
    // FFmpeg argument for scan mode
    string ffmpegScanArg = (_options.ScanMode == "fast") ? " -frames:v 1 " : " ";

        // Print scan summary and settings
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"Performing a {_options.ScanMode} scan of directory: {_options.Directory}");
        Console.WriteLine($"Using {_options.FileThreads} parallel threads, max {_options.FfmpegInstances} FFmpeg processes");
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Formats to scan: {string.Join(", ", videoExtensions)}");
        Console.WriteLine($"Recursive: {_options.Recursive}");
        Console.WriteLine($"Total files found: {totalFiles}");
        Console.WriteLine($"FFmpeg Command: ffmpeg -v error -i <filename>{ffmpegScanArg}-f null -");
        // Reserve lines for progress bar and file display
        displayLines = _options.FileThreads + 2; // 1 for header, 1 for spacing
        progressBarTop = Console.CursorTop;
        Console.SetCursorPosition(0, progressBarTop);
        Console.WriteLine(new string(' ', Console.WindowWidth)); // Progress bar line
        Console.WriteLine("Currently processing:");
        for (int i = 0; i < _options.FileThreads; i++)
        {
            Console.WriteLine(new string(' ', Console.WindowWidth)); // File display lines
        }

    // Bags to store scan results
    var majorErrors = new ConcurrentBag<(string File, string Info)>();
    var minorErrors = new ConcurrentBag<(string File, string Info)>();
    var cleanFiles = new ConcurrentBag<string>();

    // Thread-safe dictionary to track files currently being processed
    var currentlyProcessing = new ConcurrentDictionary<string, byte>();

    // Array to track which file each thread is processing
    var threadFiles = new string?[_options.FileThreads];

        void UpdateProgressBar()
        {
            // Update the progress bar and file display in-place
            lock (_consoleLock)
            {
                int bufferHeight = Console.BufferHeight;
                int safeProgressBarTop = Math.Min(progressBarTop, bufferHeight - displayLines - 1);
                int barWidth = 30;
                double percent = (double)processedFiles / totalFiles;
                int fill = (int)(percent * barWidth);

                // Draw progress bar
                Console.SetCursorPosition(0, safeProgressBarTop);
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

                // Display currently processing files below the progress bar
                int maxDisplay = _options.FileThreads;
                int line = 0;
                int baseLine = safeProgressBarTop + 1;
                if (baseLine < bufferHeight)
                {
                    Console.SetCursorPosition(0, baseLine);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("Currently processing:");
                }
                foreach (var file in currentlyProcessing.Keys.Take(maxDisplay))
                {
                    int fileLine = baseLine + 1 + line;
                    if (fileLine < bufferHeight)
                    {
                        Console.SetCursorPosition(0, fileLine);
                        Console.Write(new string(' ', Console.WindowWidth)); // Clear line
                        Console.SetCursorPosition(0, fileLine);
                        Console.Write($"  {Path.GetFileName(file)}");
                    }
                    line++;
                }
                // Clear any remaining lines if fewer files than threads
                for (; line < maxDisplay; line++)
                {
                    int fileLine = baseLine + 1 + line;
                    if (fileLine < bufferHeight)
                    {
                        Console.SetCursorPosition(0, fileLine);
                        Console.Write(new string(' ', Console.WindowWidth));
                    }
                }
                // Display thread-specific file processing
                for (int i = 0; i < _options.FileThreads; i++)
                {
                    int fileLine = safeProgressBarTop + 2 + i;
                    if (fileLine < Console.BufferHeight)
                    {
                        Console.SetCursorPosition(0, fileLine);
                        Console.Write(new string(' ', Console.WindowWidth)); // Clear line
                        Console.SetCursorPosition(0, fileLine);
                        if (threadFiles[i] != null)
                        {
                            Console.Write($"Thread {i + 1}: {Path.GetFileName(threadFiles[i])}");
                        }
                    }
                }
                Console.ResetColor();
                // Move cursor below display
                int endLine = safeProgressBarTop + displayLines;
                if (endLine < bufferHeight)
                    Console.SetCursorPosition(0, endLine);
            }
        }

        // Print the first file being scanned (for user feedback)
        if (allFiles.Count > 0)
        {
            Console.WriteLine($"Scanning first file: {allFiles[0]}");
        }

    // Semaphore to limit file threads and ffmpeg processes
    using var semaphore = new SemaphoreSlim(_options.FileThreads);
    using var ffmpegSemaphore = new SemaphoreSlim(_options.FfmpegInstances);

        // Create and start scan tasks for each file
        var tasks = allFiles.Select(async file =>
        {
            int threadIndex = -1;
            await semaphore.WaitAsync(); // Wait for file thread slot
            // Find an available thread slot
            lock (_consoleLock)
            {
                for (int i = 0; i < threadFiles.Length; i++)
                {
                    if (threadFiles[i] == null)
                    {
                        threadFiles[i] = file;
                        threadIndex = i;
                        break;
                    }
                }
            }
            try
            {
                bool major = false;
                bool minor = false;
                string errorInfo = "";

                string ext = Path.GetExtension(file);
                if (videoExtensions.Contains(ext))
                {
                    await ffmpegSemaphore.WaitAsync(); // Wait for ffmpeg process slot
                    try
                    {
                        // Start ffmpeg process to scan file
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
#if WINDOWS
                            proc.PriorityClass = ProcessPriorityClass.BelowNormal;
#endif
                        }
                        catch
                        {
                            // On Linux/macOS priority may fail, ignore
                        }

                        // Read errors from ffmpeg
                        string errors = await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();

                        if (!string.IsNullOrEmpty(errors))
                        {
                            string errLower = errors.ToLowerInvariant();
                            // Load patterns from external files (once per run)
                            if (_majorPatterns == null) _majorPatterns = LoadPatterns("MajorErrorPatterns.txt");
                            if (_minorPatterns == null) _minorPatterns = LoadPatterns("MinorErrorPatterns.txt");
                            // Check for major error
                            if (_majorPatterns.Any(p => errLower.Contains(p)))
                            {
                                major = true;
                            }
                            // Check for minor error
                            else if (_minorPatterns.Any(p => errLower.Contains(p)))
                            {
                                minor = true;
                            }
                            // Fallback: treat other errors as minor unless major already set
                            else if (!major)
                            {
                                minor = true;
                            }
                            errorInfo = errors.Trim().Replace("\r\n", "; ");
                        }
                    }
                    finally
                    {
                        ffmpegSemaphore.Release(); // Release ffmpeg process slot
                    }
                }

                // Store scan result
                if (major) majorErrors.Add((file, errorInfo));
                else if (minor) minorErrors.Add((file, errorInfo));
                else cleanFiles.Add(file);

                // Update progress bar if needed
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
                // Clear thread slot when done
                if (threadIndex >= 0)
                {
                    lock (_consoleLock)
                    {
                        threadFiles[threadIndex] = null;
                    }
                }
                semaphore.Release(); // Release file thread slot
            }
        });

        // Wait for all scan tasks to finish
        await Task.WhenAll(tasks);

        // Clear the lines from the console by overwriting with spaces
        lock (_consoleLock)
        {
            int bufferHeight = Console.BufferHeight;
            int safeProgressBarTop = Math.Min(progressBarTop, bufferHeight - displayLines - 1);
            int baseLine = safeProgressBarTop + 1;
            int totalClearLines = 1 + _options.FileThreads; // "Currently processing:" + file slots
            for (int i = 0; i < totalClearLines; i++)
            {
                int lineToClear = baseLine + i;
                if (lineToClear < bufferHeight)
                {
                    Console.SetCursorPosition(0, lineToClear);
                    Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, lineToClear); // Move cursor to start of cleared line
                }
            }
        }

        // Print scan complete message
        Console.WriteLine("\nScan complete!");

        // Write scan results to output file
        string outputDir = Path.Combine(Environment.CurrentDirectory, "BorkScans");
        Directory.CreateDirectory(outputDir);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outputFile = Path.Combine(outputDir, $"BorkScan_{timestamp}.txt");

        using var writer = new StreamWriter(outputFile);
        writer.WriteLine("=== MAJOR ERRORS ===");
        foreach (var e in majorErrors)
        {
            writer.WriteLine($"File: {e.File}");
            writer.WriteLine("Error(s):");
            foreach (var line in e.Info.Split(';'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    writer.WriteLine($"  - {trimmed}");
            }
            writer.WriteLine();
        }

        writer.WriteLine("=== MINOR ERRORS ===");
        foreach (var e in minorErrors)
        {
            writer.WriteLine($"File: {e.File}");
            writer.WriteLine("Error(s):");
            foreach (var line in e.Info.Split(';'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    writer.WriteLine($"  - {trimmed}");
            }
            writer.WriteLine();
        }

        writer.WriteLine("=== CLEAN FILES ===");
        foreach (var f in cleanFiles)
            writer.WriteLine(f);

        Console.WriteLine($"Output written to: {outputFile}");
    }
}
