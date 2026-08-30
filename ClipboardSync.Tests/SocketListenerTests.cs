using System.Net;
using System.Net.Sockets;
using ClipboardSyncApp.Core;

namespace ClipboardSync.Tests;

public class SocketListenerTests
{
    [Fact]
    public void Start_WhenPortInUse_ShouldNotThrowAndShouldReportFailure()
    {
        var port = GetFreeTcpPort();
        using var firstListener = new TcpListener(IPAddress.Any, port);
        firstListener.Start();

        var engine = new ClipboardSyncEngine { Port = port };
        var statusMessages = new List<string>();
        engine.StatusChanged += (_, message) => statusMessages.Add(message);

        var ex = Record.Exception(() => engine.Start());

        Assert.Null(ex);
        Assert.Contains(statusMessages, message =>
            message.Contains("Failed to start local listener", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already in use", StringComparison.OrdinalIgnoreCase));

        engine.Stop();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
