namespace Lantern.Feasibility;

/// <summary>
/// Lantern Phase 0 feasibility tool.
///
/// Usage:
///   dotnet run                         Run all feasibility tests
///   dotnet run -- --meili              Meilisearch connectivity test only
///   dotnet run -- --extract <path>     Extract text from a single document
///   dotnet run -- --generate-samples   Generate sample Office documents in samples/documents/
///
/// Environment variables:
///   MEILI_HOST        Meilisearch host URL (default: http://localhost:7700)
///   MEILI_MASTER_KEY  Meilisearch master key (required for Meilisearch tests)
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Lantern Feasibility Tool — Phase 0");
        Console.WriteLine("===================================");

        bool runMeili = false;
        bool runExtract = false;
        bool generateSamples = false;
        string? extractPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--meili":
                    runMeili = true;
                    break;
                case "--extract":
                    runExtract = true;
                    if (i + 1 < args.Length)
                        extractPath = args[++i];
                    else
                    {
                        Console.Error.WriteLine("--extract requires a file path argument.");
                        return 2;
                    }
                    break;
                case "--generate-samples":
                    generateSamples = true;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintHelp();
                    return 2;
            }
        }

        bool runAll = !runMeili && !runExtract && !generateSamples;

        if (generateSamples || runAll)
        {
            string samplesDir = FindSamplesDirectory();
            DocumentGenerator.GenerateSamples(samplesDir);
        }

        bool meiliPassed = true;

        if (runMeili || runAll)
        {
            string host = Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700";
            string? masterKey = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");

            if (string.IsNullOrWhiteSpace(masterKey))
            {
                Console.WriteLine();
                Console.WriteLine("WARNING: MEILI_MASTER_KEY environment variable is not set.");
                Console.WriteLine("         Meilisearch tests will be skipped.");
                Console.WriteLine("         Set MEILI_MASTER_KEY to run connectivity tests.");
                meiliPassed = false;
            }
            else
            {
                var meili = new MeilisearchFeasibility(host, masterKey);
                meiliPassed = await meili.RunAsync();
            }
        }

        if (runExtract || runAll)
        {
            var extraction = new DocumentExtractionFeasibility();
            // When running all, pass null so it scans samples/documents/
            await extraction.RunAsync(runAll ? null : extractPath);
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");

        if (runMeili || runAll)
            Console.WriteLine($"  Meilisearch : {(meiliPassed ? "PASS" : "FAIL / SKIPPED")}");

        Console.WriteLine();
        return meiliPassed ? 0 : 1;
    }

    private static string FindSamplesDirectory()
    {
        string? dir = Directory.GetCurrentDirectory();
        for (int depth = 0; depth < 8; depth++)
        {
            if (dir == null) break;
            string candidate = Path.Combine(dir, "samples", "documents");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: create relative to current dir
        string fallback = Path.Combine(Directory.GetCurrentDirectory(), "samples", "documents");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run                         Run all feasibility tests");
        Console.WriteLine("  dotnet run -- --meili              Meilisearch connectivity test only");
        Console.WriteLine("  dotnet run -- --extract <path>     Extract text from a single document");
        Console.WriteLine("  dotnet run -- --generate-samples   Generate sample Office documents");
        Console.WriteLine();
        Console.WriteLine("Environment variables:");
        Console.WriteLine("  MEILI_HOST        Meilisearch URL (default: http://localhost:7700)");
        Console.WriteLine("  MEILI_MASTER_KEY  Meilisearch master key");
    }
}
