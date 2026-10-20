using Microsoft.Extensions.Logging;

namespace Lantern.Crawling.Web;

/// <summary>
/// Fetches, parses, and caches robots.txt policies per origin (RFC 9309).
/// A missing or unreachable robots.txt means all paths are allowed.
/// </summary>
public sealed class RobotsPolicy(string userAgent, ILogger logger)
{
    private readonly Dictionary<string, (List<RobotsRule> Rules, DateTimeOffset Cached)> _cache = new();
    private readonly TimeSpan _ttl = TimeSpan.FromHours(24);

    public async Task<bool> IsAllowedAsync(string url, HttpClient client, CancellationToken ct)
    {
        if (!UrlNormalizer.TryGetOrigin(url, out string origin)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u)) return true;

        List<RobotsRule> rules = await GetRulesAsync(origin, client, ct);
        return IsAllowed(rules, u.PathAndQuery);
    }

    private async Task<List<RobotsRule>> GetRulesAsync(string origin, HttpClient client, CancellationToken ct)
    {
        if (_cache.TryGetValue(origin, out var cached) && DateTimeOffset.UtcNow - cached.Cached < _ttl)
            return cached.Rules;

        List<RobotsRule> rules = await FetchAndParseAsync(origin, client, ct);
        _cache[origin] = (rules, DateTimeOffset.UtcNow);
        return rules;
    }

    private async Task<List<RobotsRule>> FetchAndParseAsync(string origin, HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync($"{origin}/robots.txt", HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode) return [];
            string text = await response.Content.ReadAsStringAsync(ct);
            return Parse(text);
        }
        catch
        {
            return [];
        }
    }

    private bool IsAllowed(List<RobotsRule> rules, string pathAndQuery)
    {
        // Find the most-specific matching rule
        string? bestMatch = null;
        bool bestAllowed = true;

        foreach (RobotsRule rule in rules)
        {
            if (!PathMatches(pathAndQuery, rule.Path)) continue;
            // Longer path = more specific = takes precedence
            if (bestMatch == null || rule.Path.Length > bestMatch.Length)
            {
                bestMatch = rule.Path;
                bestAllowed = rule.Allow;
            }
        }
        return bestAllowed;
    }

    private List<RobotsRule> Parse(string robotsTxt)
    {
        var rules = new List<RobotsRule>();
        bool inRelevantGroup = false;

        foreach (string rawLine in robotsTxt.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('#')) continue;
            int commentIdx = line.IndexOf('#');
            if (commentIdx >= 0) line = line[..commentIdx].Trim();
            if (string.IsNullOrEmpty(line)) { /* blank line ends group — don't reset yet */ continue; }

            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                string agent = line["User-agent:".Length..].Trim();
                inRelevantGroup = agent == "*"
                    || userAgent.Contains(agent, StringComparison.OrdinalIgnoreCase)
                    || agent.Equals("LanternCrawler", StringComparison.OrdinalIgnoreCase);
            }
            else if (inRelevantGroup)
            {
                if (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["Disallow:".Length..].Trim();
                    if (!string.IsNullOrEmpty(path))
                        rules.Add(new RobotsRule(path, Allow: false));
                }
                else if (line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["Allow:".Length..].Trim();
                    if (!string.IsNullOrEmpty(path))
                        rules.Add(new RobotsRule(path, Allow: true));
                }
            }
        }
        return rules;
    }

    private static bool PathMatches(string requestPath, string robotsPattern)
    {
        if (robotsPattern == "/") return true;
        if (robotsPattern.Contains('*'))
        {
            // Simple glob: replace * with .*
            string regex = "^" + System.Text.RegularExpressions.Regex.Escape(robotsPattern).Replace(@"\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(requestPath, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return requestPath.StartsWith(robotsPattern, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RobotsRule(string Path, bool Allow);
}
