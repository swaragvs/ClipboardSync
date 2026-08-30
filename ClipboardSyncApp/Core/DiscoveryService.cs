using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace ClipboardSyncApp.Core;

public sealed class DiscoveryService
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "peer",
        "hostname",
        "hostnames",
        "tailscaleips",
        "tailscale",
        "ip",
        "ips",
        "status",
        "json",
        "data",
        "node",
        "nodes",
        "name",
        "type"
    };

    public List<string> DiscoverPeerCandidates()
    {
        var list = new List<string>();

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "tailscale",
                    Arguments = "status --json",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                list.AddRange(ParseTailnetOutput(output));
            }
        }
        catch
        {
            // Gracefully ignore missing Tailscale CLI and fall back to no discovery.
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> ParseForTests(string json)
    {
        return ParseTailnetOutput(json).ToList();
    }

    private static IEnumerable<string> ParseTailnetOutput(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        var candidates = new List<string>();
        var strings = Regex.Matches(json, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        foreach (var value in strings)
        {
            if (IsCandidate(value))
            {
                candidates.Add(value);
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || ReservedWords.Contains(value))
        {
            return false;
        }

        if (IPAddress.TryParse(value, out _))
        {
            return true;
        }

        if (value.Contains('.') || value.Contains(':'))
        {
            return value.Count(ch => ch == '.') >= 1 || value.Count(ch => ch == ':') >= 1;
        }

        return value.Length >= 2
            && value.Length <= 63
            && Regex.IsMatch(value, "^[A-Za-z0-9-]+$");
    }
}
