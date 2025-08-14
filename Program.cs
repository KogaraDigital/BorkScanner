using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        bool recursive = true; // default
        string scanMode = "full"; // default
        int maxThreads = Environment.ProcessorCount;

        // Scan info
        Console.WriteLine($"Scanning directory: {directory}");
        Console.WriteLine($"Recursive: {recursive}");
        Console.WriteLine($"Scan mode: {scanMode}");
        Console.WriteLine($"Using {maxThreads} parallel threads");

        var allFiles = Directory.EnumerateFiles(directory, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                                .Where(f => f.EndsWith(".mp4") || f.EndsWith(".mkv") || f.EndsWith(".avi") ||
                                            f.EndsWith(".jpg") || f.EndsWith(".png") || f.EndsWith(".gif") ||
                                            f.EndsWith(".mp3") || f.EndsWith(".wav"))
                                .ToList();

        int totalFiles = allFiles.Count;
        int processedFiles = 0;

        var majorErrors = new ConcurrentBag<string>();
        var minorErrors = new ConcurrentBag<string>();
        var cleanFiles = new ConcurrentBag<string>();

        // Helper for color-coded output
        void WriteProgress()
        {
            lock (_consoleLock)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                int barWidth = 30;
                double percent = (double)processedFiles / totalFiles;
                int fill = (int)(percent * barWidth);

                // Draw bar
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[");
                Console.Write(new string('█', fill));
                Console.Write(new string(' ', barWidth - fill));
                Console.Write("] ");

                // Percent text
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{percent * 100:0.0}% | ");

                // Counts
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"Major: {majorErrors.Count} | ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"Minor: {minorErrors.Count} | ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"Clean: {cleanFiles.Count}   "); // extra spaces to overwrite old text

                Console.ResetColor();
            }
        }

        await Task.WhenAll(allFiles.Select(file => Task.Run(async () =>
        {
            bool major = false;
            bool minor = false;

            if (file.EndsWith(".mp4") || file.EndsWith(".mkv") || file.EndsWith(".avi"))
            {
                string cmd = scanMode == "quick"
                    ? $"ffprobe -v error \"{file}\""
                    : $"ffmpeg -v error -i \"{file}\" -f null -";

                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo("cmd", $"/c {cmd}")
                    {
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string errors = await proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();

                if (!string.IsNullOrEmpty(errors))
                {
                    major = errors.Contains("moov") || errors.Contains("could not");
                    minor = !major;
                }
            }
            else if (file.EndsWith(".jpg") || file.EndsWith(".png") || file.EndsWith(".gif"))
            {
                try
                {
                    using (var fs = File.OpenRead(file))
                    {
                        byte[] buffer = new byte[10];
                        await fs.ReadAsync(buffer, 0, buffer.Length);
                        if (buffer.All(b => b == 0)) major = true;
                    }
                }
                catch { major = true; }
            }
            else if (file.EndsWith(".mp3") || file.EndsWith(".wav"))
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo("ffprobe", $"-v error \"{file}\"")
                    {
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string errors = await proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();
                if (!string.IsNullOrEmpty(errors)) major = true;
            }

            if (major) majorErrors.Add(file);
            else if (minor) minorErrors.Add(file);
            else cleanFiles.Add(file);

            int done = System.Threading.Interlocked.Increment(ref processedFiles);
            WriteProgress();
        })));

        Console.WriteLine("\nScan complete!");

        string outputDir = Path.Combine(directory, "BorkScans");
        Directory.CreateDirectory(outputDir);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outputFile = Path.Combine(outputDir, $"BorkScan_{timestamp}.txt");

        using (var writer = new StreamWriter(outputFile))
        {
            writer.WriteLine("=== MAJOR ERRORS ===");
            foreach (var f in majorErrors) writer.WriteLine(f);

            writer.WriteLine("\n=== MINOR ERRORS ===");
            foreach (var f in minorErrors) writer.WriteLine(f);

            writer.WriteLine("\n=== CLEAN FILES ===");
            foreach (var f in cleanFiles) writer.WriteLine(f);
        }

        Console.WriteLine($"Output written to: {outputFile}");
    }
}
