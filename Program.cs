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

        // ARGUMENTS
        //  REQUIRED
        //      directory (filepath)
        //      scan mode (full/fast) default: full
        //  OPTIONAL
        //      --fileThreads (int) default: logical processors / 2
        //      --ffmpegInstances (int) default: 4

        string directory = args[0];
        string fullScanArgument = (args[1].ToLower() == "fast") ? " " : " -frames:v 1 ";

        int fileThreads = Environment.ProcessorCount / 2;
        int ffmpegInstances = 4; // Limit FFmpeg concurrency

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--filethreads":
                    if (i + 1 < args.Length) fileThreads = int.Parse(args[++i]);
                    break;
                case "--ffmpeginstances":
                    if (i + 1 < args.Length) ffmpegInstances = int.Parse(args[++i]);
                    break;
            }
        }



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
        List<string> allFiles;
        try
        {
            allFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
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
        Console.WriteLine($"Performing a {(args[1].ToLower() == "full" ? "full" : "quick")} scan of directory: {directory}");
        Console.WriteLine($"Using {fileThreads} parallel threads, max {ffmpegInstances} FFmpeg processes");
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Formats to scan: {string.Join(", ", allExtensions)}");
        Console.WriteLine($"Total files found: {totalFiles}");
        Console.WriteLine($"FFmpeg Command: ffmpeg -v error -i <filename>{fullScanArgument}-f null -");
        Console.WriteLine(); // Space before progress bar

        // ConcurrentBag is used to handle multiple threads dumping data at the same time
        var majorErrors = new ConcurrentBag<(string File, string Info)>();
        var minorErrors = new ConcurrentBag<(string File, string Info)>();
        var cleanFiles = new ConcurrentBag<string>();

        // Logic for updating the UI while scanning
        void UpdateProgressBar()
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
        using var semaphore = new SemaphoreSlim(fileThreads);
        using var ffmpegSemaphore = new SemaphoreSlim(ffmpegInstances);



        var tasks = allFiles.Select(async file =>
        {
            // Create main semaphore task
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
                    // Create sub semaphore for ffmpeg limiting
                    await ffmpegSemaphore.WaitAsync();
                    try
                    {
                        var proc = new Process
                        {
                            // Create the ffmpeg command
                            StartInfo = new ProcessStartInfo(
                            "ffmpeg", $"-v error -i \"{file}\"{fullScanArgument}-f null -")// -threads 1")  // Uncomment if CPU usage becomes an issue, hacky fix
                            {
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        // Start the process with a lowered priority class, this only works for windows
                        // TODO: Add configuriation for linux/macOS machines
                        proc.Start();
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                        string errors = await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();

                        // Filters out common major errors and marks anything else as minor
                        // TODO: Add more robust filtering
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

                Interlocked.Increment(ref processedFiles);
                UpdateProgressBar();
            }
            finally
            {
                semaphore.Release();
            }
        });

        // Wait for all tasks to complete
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
