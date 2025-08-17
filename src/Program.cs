
using System;
using System.Threading.Tasks;

class Program
{
    // Entry point for the CLI application
    static async Task Main(string[] args)
    {
        // Show help if no arguments or --help is provided
        if (args.Length < 1 || args.Contains("--help"))
        {
            HelpPrinter.PrintHelp();
            return;
        }

        // Parse command line arguments into ScanOptions
        var options = ScanOptions.Parse(args);
        if (options == null)
        {
            HelpPrinter.PrintHelp();
            return;
        }

        // Create scanner and start scan
        var scanner = new Scanner(options);
        await scanner.RunAsync();
    }
}
