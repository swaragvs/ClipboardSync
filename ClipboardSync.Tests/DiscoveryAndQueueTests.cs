using ClipboardSyncApp.Core;

namespace ClipboardSync.Tests;

public class DiscoveryAndQueueTests
{
    [Fact]
    public void DiscoveryService_ParseTailnetOutput_ShouldExtractPeerIpsAndHostnames()
    {
        var json = """
        {
          "Peer" : [
            {
              "HostName": "laptop",
              "TailscaleIPs": ["100.64.0.12", "fd7a:115c:a1e0::12"]
            },
            {
              "HostName": "desktop",
              "TailscaleIPs": ["100.64.0.25"]
            }
          ]
        }
        """;

        var peers = DiscoveryService.ParseForTests(json);

        Assert.Contains("100.64.0.12", peers);
        Assert.Contains("100.64.0.25", peers);
        Assert.Contains("laptop", peers);
    }

    [Fact]
    public void TransferQueue_Enqueue_ShouldCoalesceStaleTextAndHonorMaxDepth()
    {
        var queue = new TransferQueue(maxDepth: 2);

        Assert.True(queue.Enqueue("first"));
        Assert.True(queue.Enqueue("second"));
        Assert.True(queue.Enqueue("third"));

        Assert.Equal(2, queue.Count);
        Assert.Equal("second", queue.Dequeue());
        Assert.Equal("third", queue.Dequeue());
    }
}
