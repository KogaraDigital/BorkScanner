using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class BorkScanner
{
    private static readonly object _consoleLock = new object();

    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: BorkScanner <directory>");
            return;
        }

        string directory = args[0];
        int maxThreads = Environment.ProcessorCount / 2;
        int maxFFmpeg = 2; // Limit FFmpeg concurrency

        var formats = new[] { ".mp4", ".mkv", ".avi", ".jpg", ".png", ".gif", ".mp3", ".wav" };

        var allFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                                .Where(f => formats.Any(ext => f.EndsWith(ext)))
                                .ToList();

        int totalFiles = allFiles.Count;
        int processedFiles = 0;

        // === SUMMARY INFO ===
        Console.WriteLine($"Scanning directory: {directory}");
        Console.WriteLine($"Using {maxThreads} parallel threads, max {maxFFmpeg} FFmpeg processes");
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Formats checked: {string.Join(", ", formats)}");
        Console.WriteLine($"Total files found: {totalFiles}");
        Console.WriteLine(); // Space before progress bar

        var majorErrors = new ConcurrentBag<(string File, string Info)>();
        var minorErrors = new ConcurrentBag<(string File, string Info)>();
        var cleanFiles = new ConcurrentBag<string>();

        void WriteProgress()
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

        // First-file hack: log first file name
        if (allFiles.Count > 0)
        {
            Console.WriteLine($"Scanning first file: {allFiles[0]}");
        }

        using var semaphore = new SemaphoreSlim(maxThreads);
        using var ffmpegSemaphore = new SemaphoreSlim(maxFFmpeg);

        var tasks = allFiles.Select(async file =>
        {
            await semaphore.WaitAsync();
            try
            {
                bool major = false;
                bool minor = false;
                string errorInfo = "";

                if (file.EndsWith(".mp4") || file.EndsWith(".mkv") || file.EndsWith(".avi") ||
                    file.EndsWith(".mp3") || file.EndsWith(".wav"))
                {
                    await ffmpegSemaphore.WaitAsync();
                    try
                    {
                        string cmd = (file.EndsWith(".mp3") || file.EndsWith(".wav")) ?
                                     $"-v error \"{file}\"" :
                                     $"-v error -i \"{file}\" -f null - -threads 1";

                        var proc = new Process
                        {
                            StartInfo = new ProcessStartInfo(
                                file.EndsWith(".mp3") || file.EndsWith(".wav") ? "ffprobe" : "ffmpeg",
                                cmd)
                            {
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        proc.Start();
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                        string errors = await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();

                        if (!string.IsNullOrEmpty(errors))
                        {
                            major = file.EndsWith(".mp3") || file.EndsWith(".wav") ? true : errors.Contains("moov") || errors.Contains("could not");
                            minor = !major;
                            errorInfo = errors.Trim().Replace("\r\n", "; ");
                        }
                    }
                    finally
                    {
                        ffmpegSemaphore.Release();
                    }
                }
                else if (file.EndsWith(".jpg") || file.EndsWith(".png") || file.EndsWith(".gif"))
                {
                    try
                    {
                        using var fs = File.OpenRead(file);
                        byte[] buffer = new byte[10];
                        await fs.ReadAsync(buffer, 0, buffer.Length);
                        if (buffer.All(b => b == 0))
                        {
                            major = true;
                            errorInfo = "File content all zeros";
                        }
                    }
                    catch (Exception ex)
                    {
                        major = true;
                        errorInfo = ex.Message;
                    }
                }

                if (major) majorErrors.Add((file, errorInfo));
                else if (minor) minorErrors.Add((file, errorInfo));
                else cleanFiles.Add(file);

                Interlocked.Increment(ref processedFiles);
                WriteProgress();
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Console.WriteLine("\nScan complete!");

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
}
