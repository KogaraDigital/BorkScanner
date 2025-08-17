using System;

public static class HelpPrinter
{
    // Prints help and usage information to the console
    public static void PrintHelp()
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
