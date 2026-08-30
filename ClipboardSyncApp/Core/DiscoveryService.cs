using System.Diagnostics;
using System.Text.Json;

namespace ClipboardSyncApp.Core;

public sealed class DiscoveryService
{
    public List<string> DiscoverPeerCandidates()
    {
        var candidates = new List<string>();

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
                candidates.AddRange(ParseTailnetOutput(output));
            }
        }
        catch
        {
            // Tailscale CLI unavailable - return empty candidate list for manual entry fallback
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

        var results = new List<string>();
        var selfIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 1. Extract Self node IPs to exclude local machine
            if (root.TryGetProperty("Self", out var selfElement))
            {
                ExtractIpsAndHostnames(selfElement, selfIps, null);
            }

            // 2. Extract Peer node IPs
            if (root.TryGetProperty("Peer", out var peerElement))
            {
                if (peerElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in peerElement.EnumerateObject())
                    {
                        ExtractIpsAndHostnames(prop.Value, results, selfIps);
                    }
                }
                else if (peerElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in peerElement.EnumerateArray())
                    {
                        ExtractIpsAndHostnames(item, results, selfIps);
                    }
                }
            }
        }
        catch
        {
            // Gracefully ignore parse errors
        }

        return results.Where(x => !selfIps.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExtractIpsAndHostnames(JsonElement element, ICollection<string> destination, HashSet<string>? excludeSet)
    {
        // Skip offline nodes if Online property is false
        if (element.TryGetProperty("Online", out var onlineElem) && onlineElem.ValueKind == JsonValueKind.False)
        {
            return;
        }

        if (element.TryGetProperty("HostName", out var hostElem) && hostElem.ValueKind == JsonValueKind.String)
        {
            var host = hostElem.GetString();
            if (!string.IsNullOrWhiteSpace(host) && (excludeSet == null || !excludeSet.Contains(host)))
            {
                destination.Add(host);
            }
        }

        if (element.TryGetProperty("TailscaleIPs", out var ipsElem) && ipsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var ipElem in ipsElem.EnumerateArray())
            {
                if (ipElem.ValueKind == JsonValueKind.String)
                {
                    var ip = ipElem.GetString();
                    if (!string.IsNullOrWhiteSpace(ip) && (excludeSet == null || !excludeSet.Contains(ip)))
                    {
                        destination.Add(ip);
                    }
                }
            }
        }
    }
}
