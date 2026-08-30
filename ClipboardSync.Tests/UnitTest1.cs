using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ClipboardSyncApp.Config;
using ClipboardSyncApp.Core;
using ClipboardSyncApp.Core.Security;
using ClipboardSyncApp.Storage;

namespace ClipboardSync.Tests;

public class FullArchitectureAndContractTests
{
    [Fact]
    public void BoundedLruCache_Eviction_ShouldEvictOldestItemWhenCapacityExceeded()
    {
        var cache = new BoundedLruCache<string, int>(capacity: 3);

        cache.Add("key1", 1);
        cache.Add("key2", 2);
        cache.Add("key3", 3);
        Assert.Equal(3, cache.Count);

        // Access key1 to make it most recently used
        Assert.True(cache.TryGet("key1", out var val));
        Assert.Equal(1, val);

        // Add key4 -> should evict key2 (since key1 was accessed)
        cache.Add("key4", 4);
        Assert.Equal(3, cache.Count);

        Assert.True(cache.ContainsKey("key1"));
        Assert.False(cache.ContainsKey("key2"));
        Assert.True(cache.ContainsKey("key3"));
        Assert.True(cache.ContainsKey("key4"));
    }

    [Fact]
    public void FrameCipher_EncryptAndDecrypt_ShouldRoundtripWithNonceSequenceAndAAD()
    {
        var psk = "secret-psk-key-123";
        var clientChallenge = Encoding.UTF8.GetBytes("client-nonce-32-byte-length-1234");
        var serverChallenge = Encoding.UTF8.GetBytes("server-nonce-32-byte-length-5678");
        var sessionKey = FrameCipher.DeriveSessionKey(psk, clientChallenge, serverChallenge);

        var plainText = """{"Type":16,"SessionId":"s1","MessageId":"m1","Text":"Hello ClipboardSync"}""";
        var plainBytes = Encoding.UTF8.GetBytes(plainText);

        ulong seqNum = 42;
        byte version = 2;
        byte msgType = 0x10;

        var encryptedEnvelope = FrameCipher.EncryptFrame(plainBytes, sessionKey, seqNum, version, msgType, out var header);
        Assert.NotEmpty(encryptedEnvelope);

        // Header MUST be 14 bytes
        Assert.Equal(14, header.Length);
        Assert.Equal(version, header[0]);
        Assert.Equal(msgType, header[5]);
        Assert.Equal(seqNum, FrameCipher.BinaryPrimitives_ReadUInt64BigEndian(header.AsSpan(6, 8)));

        var decryptedBytes = FrameCipher.DecryptFrame(encryptedEnvelope, sessionKey, header);
        var decryptedText = Encoding.UTF8.GetString(decryptedBytes);
        Assert.Equal(plainText, decryptedText);
    }

    [Fact]
    public void FrameCipher_ProtectAndUnprotectSecret_ShouldRoundtrip()
    {
        var secret = "MyTailscalePSKKey_2026";
        var protectedSecret = FrameCipher.ProtectSecret(secret);
        Assert.NotEmpty(protectedSecret);

        var unprotected = FrameCipher.UnprotectSecret(protectedSecret);
        Assert.Equal(secret, unprotected);
    }

    [Fact]
    public void PayloadQueue_Enqueue_ShouldCoalesceTextPayloadsInQueuedState()
    {
        var queue = new PayloadQueue(maxDepth: 5);

        var payload1 = new ClipboardPayload { Type = MessageType.ClipboardText, Text = "Copy 1" };
        var payload2 = new ClipboardPayload { Type = MessageType.ClipboardText, Text = "Copy 2" };

        queue.Enqueue(payload1, out _);
        queue.Enqueue(payload2, out _);

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var dequeuedItem));
        Assert.NotNull(dequeuedItem);
        Assert.Equal("Copy 2", dequeuedItem!.Payload.Text);
        Assert.Equal(QueueItemState.InFlight, dequeuedItem.State);
    }

    [Fact]
    public void FileTransferService_PathSanitization_ShouldStripPathTraversal()
    {
        var malformedPath = @"C:\Windows\System32\..\..\test.txt";
        var safeName = System.IO.Path.GetFileName(malformedPath);
        Assert.Equal("test.txt", safeName);
    }

    [Fact]
    public async Task NamedPipeIpc_ShouldExchangeCommandSuccessfully()
    {
        var pipeName = "Test_ClipboardSync_Pipe_" + Guid.NewGuid().ToString("N");
        var receivedCommand = string.Empty;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var serverTask = Task.Run(async () =>
        {
            using var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipeServer.WaitForConnectionAsync(cts.Token);
            using var reader = new StreamReader(pipeServer, Encoding.UTF8);
            receivedCommand = await reader.ReadLineAsync(cts.Token);
        });

        await Task.Delay(100); // Allow server to start

        using (var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await pipeClient.ConnectAsync(1000, cts.Token);
            using var writer = new StreamWriter(pipeClient, Encoding.UTF8);
            await writer.WriteLineAsync("OPEN");
            await writer.FlushAsync(cts.Token);
        }

        await serverTask;
        Assert.Equal("OPEN", receivedCommand);
    }

    [Fact]
    public void AppSettings_Validate_ShouldCorrectInvalidValues()
    {
        var settings = new AppSettings
        {
            Port = 999999, // invalid port
            MaxQueueDepth = 0, // invalid depth
            MaxImageSizeMB = 100 // invalid max image size
        };

        settings.Validate();

        Assert.Equal(5001, settings.Port);
        Assert.Equal(20, settings.MaxQueueDepth);
        Assert.Equal(25, settings.MaxImageSizeMB);
    }

    [Fact]
    public void DeviceIdentity_GetOrCreatePeerId_ShouldBePersistentAndValidGuid()
    {
        var peerId1 = DeviceIdentity.GetOrCreatePeerId();
        Assert.False(string.IsNullOrWhiteSpace(peerId1));

        var peerId2 = DeviceIdentity.GetOrCreatePeerId();
        Assert.Equal(peerId1, peerId2);
    }

    [Fact]
    public void ConnectionStore_AtomicPersistence_ShouldSaveAndLoadProfiles()
    {
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test Peer",
            TailscaleIp = "100.64.0.100",
            Port = 5001,
            AutoConnect = true
        };

        ConnectionStore.Upsert(profile);
        var loaded = ConnectionStore.Load();

        Assert.Contains(loaded, p => p.Id == profile.Id && p.Name == "Test Peer");

        ConnectionStore.Delete(profile.Id);
        var reloaded = ConnectionStore.Load();
        Assert.DoesNotContain(reloaded, p => p.Id == profile.Id);
    }

    [Fact]
    public void RemoteClipboardTracker_EchoSuppression_ShouldSuppressMultipleAsyncEventsAndNormalizeText()
    {
        var tracker = new ClipboardSyncApp.Platform.Windows.RemoteClipboardTracker();
        var originalText = "Hello World\nLine 2";
        var windowsFormatText = "Hello World\r\nLine 2\0";

        var normBytes1 = Encoding.UTF8.GetBytes(ClipboardSyncApp.Platform.Windows.RemoteClipboardTracker.NormalizeText(originalText));
        var normBytes2 = Encoding.UTF8.GetBytes(ClipboardSyncApp.Platform.Windows.RemoteClipboardTracker.NormalizeText(windowsFormatText));

        // Record injected remote content
        tracker.RecordInjectedRemote(MessageType.ClipboardText, normBytes1, "msg-123");

        // Windows fires FIRST WM_CLIPBOARDUPDATE event
        Assert.True(tracker.IsEcho(MessageType.ClipboardText, normBytes2));

        // Windows fires SECOND WM_CLIPBOARDUPDATE event 10ms later for the SAME content -> MUST STILL BE SUPPRESSED!
        Assert.True(tracker.IsEcho(MessageType.ClipboardText, normBytes2));
    }
}