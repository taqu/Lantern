using Meilisearch;
using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.Feasibility;

/// <summary>
/// Seeds the lantern-documents index with sample documents for development/testing.
/// </summary>
internal static class Seeder
{
    private const string IndexName = "lantern-documents";

    public static async Task<bool> RunAsync()
    {
        string host = Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700";
        string? masterKey = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");

        if (string.IsNullOrWhiteSpace(masterKey))
        {
            Console.Error.WriteLine("ERROR: MEILI_MASTER_KEY environment variable is not set.");
            Console.Error.WriteLine("       Set MEILI_MASTER_KEY before running --seed.");
            return false;
        }

        Console.WriteLine($"Seeder — connecting to {host}");

        var client = new MeilisearchClient(host, masterKey);

        try
        {
            // Verify connectivity
            var health = await client.HealthAsync();
            Console.WriteLine($"  Meilisearch health: {health.Status}");

            var documents = BuildSeedDocuments();
            var indexing = new MeilisearchIndexingService(client, new MeilisearchOptions { IndexName = IndexName },
                NullLogger<MeilisearchIndexingService>.Instance);
            await indexing.UpsertBatchAsync(documents.Select(d => new SearchDocument
            {
                SourceId = "development-seed", SourceType = d.SourcePath != null ? "file" : "web",
                Path = d.SourcePath, Uri = d.Url ?? "", Title = d.Title, Content = d.Content,
                MimeType = d.SourcePath == null ? "text/html" : null
            }));

            Console.WriteLine($"  Successfully indexed {documents.Count} documents into '{IndexName}'.");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Seeding failed — {ex.Message}");
            return false;
        }
    }

    private static List<SeedDocument> BuildSeedDocuments() =>
    [
        new SeedDocument
        {
            Id = "doc-001",
            Title = "Lantern Architecture Overview",
            Content = "Lantern is a lightweight self-hosted full-text search platform for Windows and intranet environments. It uses Meilisearch as its search engine.",
            Url = "https://example.local/docs/architecture",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-002",
            Title = "Meilisearch Quick Start",
            Content = "Meilisearch is a powerful, fast, and open-source search engine. It provides a RESTful API for indexing and searching documents.",
            Url = "https://example.local/docs/meilisearch-quickstart",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-003",
            Title = "Document Extraction with Toxy",
            Content = "Toxy is a .NET library for extracting text from PDF, DOCX, XLSX, and PPTX files. Japanese text extraction is supported.",
            Url = null,
            SourcePath = @"C:\Docs\toxy-guide.pdf",
            DocumentType = "PDF"
        },
        new SeedDocument
        {
            Id = "doc-004",
            Title = "Full-Text Search Best Practices",
            Content = "Effective full-text search requires good tokenization, stemming, and relevance tuning. Meilisearch handles most of this automatically.",
            Url = "https://example.local/articles/fts-best-practices",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-005",
            Title = "Windows Deployment Guide",
            Content = "Lantern runs as a native Windows application without requiring Docker or Java. Simply extract the archive and run the executable.",
            Url = null,
            SourcePath = @"C:\Docs\deployment-guide.docx",
            DocumentType = "DOCX"
        },
        new SeedDocument
        {
            Id = "doc-006",
            Title = "ランタンの概要",
            Content = "ランタンは、Windows環境向けの軽量な全文検索プラットフォームです。Meilisearchを使用して、高速な検索を実現します。",
            Url = "https://example.local/ja/overview",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-007",
            Title = "検索インデックスの設定",
            Content = "検索インデックスを設定するには、管理画面からインデックス名とAPIキーを入力してください。クローラーを起動すると自動的に文書が収集されます。",
            Url = null,
            SourcePath = @"C:\Docs\index-config.pptx",
            DocumentType = "PPTX"
        },
        new SeedDocument
        {
            Id = "doc-008",
            Title = "Crawler Configuration",
            Content = "The Lantern crawler supports local filesystem, network shares, and web crawling. Configure allowed paths and URL patterns to restrict scope.",
            Url = "https://example.local/docs/crawler-config",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-009",
            Title = "PDF Text Extraction Notes",
            Content = "Lantern extracts text from text-based PDF documents. Scanned image PDFs are not supported in version 1.0. Password-protected PDFs will be skipped.",
            Url = null,
            SourcePath = @"C:\Reports\extraction-notes.pdf",
            DocumentType = "PDF"
        },
        new SeedDocument
        {
            Id = "doc-010",
            Title = "Network Share Indexing",
            Content = @"To index a network share, provide the UNC path such as \\server\share\documents and grant read permissions to the service account.",
            Url = null,
            SourcePath = @"\\fileserver\docs\network-guide.docx",
            DocumentType = "DOCX"
        },
        new SeedDocument
        {
            Id = "doc-011",
            Title = "Search Result Highlighting",
            Content = "Lantern highlights matching search terms in result snippets. Highlighting uses safe server-side markup to prevent XSS vulnerabilities.",
            Url = "https://example.local/docs/highlighting",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-012",
            Title = "Quarterly Sales Report Q1 2026",
            Content = "Total sales reached 12.4 million yen in Q1 2026. The North region exceeded targets by 8 percent. PDF export is available from the dashboard.",
            Url = null,
            SourcePath = @"C:\Reports\Q1-2026-sales.xlsx",
            DocumentType = "XLSX"
        },
        new SeedDocument
        {
            Id = "doc-013",
            Title = "Software Architecture Decisions",
            Content = "The team decided to use Blazor Server for the UI to avoid a separate JavaScript build pipeline. SQLite will store crawler configuration and state.",
            Url = null,
            SourcePath = @"C:\Projects\arch-decisions.docx",
            DocumentType = "DOCX"
        },
        new SeedDocument
        {
            Id = "doc-014",
            Title = "Open Source Licenses",
            Content = "Lantern uses open source components including Meilisearch (SSPL), Toxy (Apache 2.0), and the Meilisearch .NET client (MIT). Review the dependencies document.",
            Url = "https://example.local/docs/licenses",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-015",
            Title = "技術文書の検索",
            Content = "技術文書を効率的に検索するためには、適切なインデックス設定が重要です。全文検索エンジンはトークナイゼーションと関連性スコアリングを自動的に処理します。",
            Url = null,
            SourcePath = @"C:\Docs\tech-search-ja.txt",
            DocumentType = "TXT"
        },
        new SeedDocument
        {
            Id = "doc-016",
            Title = "Intranet Search Use Cases",
            Content = "Common intranet search use cases include finding company policies, technical documentation, project reports, and internal wikis.",
            Url = "https://wiki.example.local/intranet-search",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-017",
            Title = "Phase 0 Feasibility Report",
            Content = "Phase 0 validated Meilisearch on Windows x64, Toxy document extraction for DOCX/XLSX/PPTX, and the Meilisearch .NET client version 0.20.0.",
            Url = null,
            SourcePath = @"C:\Projects\Lantern\docs\feasibility.md",
            DocumentType = "TXT"
        },
        new SeedDocument
        {
            Id = "doc-018",
            Title = "PowerPoint Slide Extraction Test",
            Content = "This presentation contains three slides with English and Japanese text. Slide extraction was verified using the Toxy library on .NET 10.",
            Url = null,
            SourcePath = @"C:\Tests\sample.pptx",
            DocumentType = "PPTX"
        },
        new SeedDocument
        {
            Id = "doc-019",
            Title = "Configuration Reference",
            Content = "All configuration values can be set in appsettings.json or overridden with environment variables. Sensitive values such as API keys should use environment variables.",
            Url = "https://example.local/docs/configuration",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-020",
            Title = "Roadmap and Future Features",
            Content = "Planned features include web crawling, scheduled indexing, authentication, and an administration dashboard. Version 2.0 will add OCR support.",
            Url = "https://example.local/docs/roadmap",
            SourcePath = null,
            DocumentType = "HTML"
        },
        new SeedDocument
        {
            Id = "doc-021",
            Title = "データエクスポート機能",
            Content = "検索結果をCSVまたはJSONフォーマットでエクスポートすることができます。エクスポートには検索クエリと一致するすべての文書が含まれます。",
            Url = null,
            SourcePath = @"C:\Docs\export-feature-ja.xlsx",
            DocumentType = "XLSX"
        },
        new SeedDocument
        {
            Id = "doc-022",
            Title = "Security and Access Control",
            Content = "Meilisearch API keys are never sent to browser clients. All search requests are proxied through the ASP.NET Core server to prevent key exposure.",
            Url = "https://example.local/docs/security",
            SourcePath = null,
            DocumentType = "HTML"
        },
    ];

    private sealed class SeedDocument
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Url { get; set; }
        public string? SourcePath { get; set; }
        public string? DocumentType { get; set; }
    }
}
