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
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: BorkScanner <directory> <full/quick>");
            return;
        }

        string directory = args[0];
        string fullScanArgument = (args[1].ToLower() == "full") ? " " : " -frames:v 1 ";
        int maxThreads = Environment.ProcessorCount / 2;
        int maxFFmpeg = 2; // Limit FFmpeg concurrency

        //var formats = new[] { ".mp4", ".mkv", ".avi", ".jpg", ".png", ".gif", ".mp3", ".wav", ".jpeg" };

        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".webm"
        };

        // Unused currently, but may be needed if adding image validation in future
        /*var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif"
        };*/

        // Smash all extensions into one HashSet for full scans, image extension is commented out so this does nothing right now, XD
        var allExtensions = videoExtensions
        //.Concat(imageExtensions)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Create a directory of all files within the specified directory string
        // TODO: Create argument to toggle recursive search
        var allFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
        .Where(f => allExtensions.Contains(Path.GetExtension(f)))
        .ToList();

        int totalFiles = allFiles.Count;
        int processedFiles = 0;

        // Print scan info to console before starting scan
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"Scanning directory: {directory}");
        Console.WriteLine($"Using {maxThreads} parallel threads, max {maxFFmpeg} FFmpeg processes");
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Formats to scan: {string.Join(", ", allExtensions)}");
        Console.WriteLine($"Total files found: {totalFiles}");
        Console.WriteLine($"FFMPEG Command: ffmpeg -v error -i <filename>{fullScanArgument}-f null -");
        Console.WriteLine(); // Space before progress bar

        // Concurrent bag is used to handle multiple threads dumping data at the same time
        // This creates an unordered collection of error infomation
        var majorErrors = new ConcurrentBag<(string File, string Info)>();
        var minorErrors = new ConcurrentBag<(string File, string Info)>();
        var cleanFiles = new ConcurrentBag<string>();

        // Logic for updating the UI while scanning
        void WriteProgress()
        {
            lock (_consoleLock)
            {
                // Draw the progress bar
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

                // Display scan stats
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

        // Prints a line to notify that the scan has started, useful for when the first files scanned take a while to confirm the system isn't hanging
        if (allFiles.Count > 0)
        {
            Console.WriteLine($"Scanning first file: {allFiles[0]}");
        }

        // Semaphore is used to handle threads, this is set to 2 by default to limit CPU usage
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

                // Get the extension of the file
                string ext = Path.GetExtension(file);
                // If extension is valid begin check
                if (videoExtensions.Contains(ext))
                {
                    await ffmpegSemaphore.WaitAsync();
                    try
                    {
                        var proc = new Process
                        {
                            StartInfo = new ProcessStartInfo(
                            "ffmpeg", $"-v error -i \"{file}\"{fullScanArgument}-f null -")// -threads 1")  // Uncomment if CPU usage becomes an issue, hacky fix
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

        // Output scan data once scanning is complete
        Console.WriteLine("\nScan complete!");

        // Write the error file to a folder in the execution directory
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
