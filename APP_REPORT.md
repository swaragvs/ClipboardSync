# ClipboardSync v2 — Comprehensive Application Report

## 1. Executive Summary
**ClipboardSync v2** is a lightweight, secure, peer-to-peer Windows desktop application designed for real-time clipboard synchronization (text, PNG images, RTF rich text, and 2-way file transfers) between trusted devices over a private Tailscale network. 

Built using **.NET 8** and **Windows Forms**, the application features a 100% headless-safe core engine, authenticated AES-GCM encrypted transport framing, stable installation identity, atomic JSON persistence, and a system tray lifecycle shell.

---

## 2. Architectural Blueprint & Project Layout

The codebase enforces strict separation between the platform-agnostic P2P engine (`Core/`), platform implementations (`Platform/Windows/`), persistence stores (`Storage/`), configuration management (`Config/`), and WinForms views (`UI/`).

```text
ClipboardSyncApp/
├── Program.cs                         # Single-instance Mutex + Named Pipe IPC server ("ClipboardSync_IPC_Pipe")
├── Config/
│   └── AppSettings.cs                 # Persisted & validated configuration (Port, MaxQueueDepth, MaxImageSizeMB, ReceivedFolder, HistoryMaxItems)
├── Core/                              # 100% HEADLESS-SAFE ENGINE (Zero WinForms references)
│   ├── IClipboardService.cs           # Clipboard data extraction & injection abstraction
│   ├── IClipboardWatcher.cs           # Clipboard change notification event abstraction
│   ├── ILogger.cs                     # Core logging abstraction interface
│   ├── DeviceIdentity.cs              # Persistent 128-bit Installation PeerId manager
│   ├── BoundedLruCache.cs             # Dictionary + LinkedList 100-item LRU cache with TTL support
│   ├── ClipboardPayload.cs            # Wire payload models, envelopes, & MessageType enums
│   ├── ClipboardSyncEngine.cs         # Master P2P sync engine, event router, & pause semantics
│   ├── PeerConnection.cs              # Persistent TCP session, 14B header framing, AES-GCM cipher, Ping/Pong keepalive
│   ├── PeerManager.cs                 # Thread-safe profile manager & deterministic dual-session arbitration
│   ├── DiscoveryService.cs            # Tailscale status JSON parser (excluding self nodes and exit nodes)
│   ├── PayloadQueue.cs                # Outbound queue: states (Queued, InFlight), text coalescing, backpressure caps
│   ├── FileTransferService.cs         # 2-way file transfer registry, pre-flight resource checks, atomic .partial rename, SHA-256
│   └── Security/
│       ├── HandshakeService.cs        # HKDF-SHA256 session key derivation & mutual challenge authentication
│       └── FrameCipher.cs             # AES-GCM frame cipher with 14B AAD header validation & sequence-numbered nonces
├── Storage/
│   ├── ConnectionProfile.cs           # Saved peer configuration (Id, Name, TailscaleIp, Port, AutoConnect, SharedKey)
│   ├── ConnectionStore.cs             # Atomic JSON persistence with DPAPI PSK protection & corruption recovery
│   └── ClipboardHistoryStore.cs       # Persistent SQLite clipboard history database
├── Platform/
│   └── Windows/                       # Windows-specific implementations
│       ├── WindowsClipboardService.cs # STA thread marshalled WinForms Clipboard API wrapper (Text, Image, RTF)
│       ├── WindowsClipboardWatcher.cs # NativeWindow HWND WM_CLIPBOARDUPDATE listener with lifecycle management
│       └── RemoteClipboardTracker.cs  # SHA-256 content-hash & message ID echo suppression tracker (100 items, 5s TTL)
└── UI/                                # WinForms Presentation Layer
    ├── TrayContext.cs                 # System tray NotifyIcon, balloon tips, & context menu
    ├── MainForm.cs                    # Main application dashboard
    ├── PeerManagerForm.cs             # Saved connection manager dialog with live online/offline badges
    ├── HistoryForm.cs                 # Searchable clipboard history browser
    └── SettingsForm.cs                # Application configuration dialog
```

---

## 3. Key Technical & Functional Features

### A. Protocol & Wire Envelope Specification
- **14-Byte Unencrypted Header**:
  ```text
  [Version(1B)][Length(4B BE)][MessageType(1B)][SequenceNumber(8B BE)]
  ```
- **Authenticated Additional Data (AAD)**: The exact 14-byte unencrypted header is passed as AAD to `AES-GCM`.
- **Nonce & Sequence Protection**: Nonces are generated using a 96-bit random session prefix XORed with a 64-bit sequence counter. Inbound frames with `SequenceNumber <= LastReceivedSequence` are dropped to prevent replay attacks.

### B. Security & Cryptography
- **HKDF-SHA256 Session Key Derivation**: 256-bit AES session keys are derived from a pre-shared key (PSK) and mutual 32-byte challenge nonces (`HKDF-Expand(HKDF-Extract(salt=Challenges, IKM=PSK), info="ClipboardSync-v2-AES-GCM", L=32)`).
- **DPAPI Key Protection**: Saved pre-shared keys in `peers.json` are encrypted using Windows DPAPI (`ProtectedData.Protect`).
- **Strict Authentication Gate**: Sockets reject all non-handshake frames prior to reaching the `Authenticated` state.

### C. Persistent Installation Identity & Connection Arbitration
- **Stable PeerId (`DeviceIdentity.cs`)**: 128-bit GUID generated once on initial startup and saved in `%AppData%\ClipboardSync\device_identity.json` (DPAPI protected). Persistent across restarts without depending on IP or hostname.
- **Deterministic Dual-Session Resolution**: Post-authentication comparison (`LocalPeerId < RemotePeerId`). The lower `PeerId` retains its outbound connection while the duplicate inbound connection is cleanly closed.

### D. Rich Clipboard Synchronization
- **Multi-Format Support**: Plain text, raw PNG binary images, and RTF rich text.
- **Echo Suppression (`RemoteClipboardTracker.cs`)**: Thread-safe 100-item LRU cache with a 5-second TTL that tracks injected content hashes and suppresses Windows `WM_CLIPBOARDUPDATE` feedback loops.

### E. 2-Way File Transfer Protocol
- **Protocol Flow**: `FileOffer` $\rightarrow$ `FileAccept` $\rightarrow$ 64KB `FileChunk` streaming $\rightarrow$ `FileComplete` with SHA-256 verification.
- **Path Traversal Security**: Senders NEVER transmit local paths (`LocalTransferRegistry`). File names are sanitized via `Path.GetFileName()`.
- **Pre-Flight Limit Checks**: Rejects offers exceeding `MaxIncomingFileSizeMB` (default 2 GB) before creating files on disk.
- **Atomic File Renaming & Cleanup**: Chunks stream into `<TransferId>.partial`. Upon successful SHA-256 verification, atomic move to `<FileName>`. Interrupted or cancelled transfers delete temp files immediately.

### F. Reliability, Bounds, & Storage Durability
- **Atomic JSON Persistence**: Stores (`peers.json`, `settings.json`, `device_identity.json`) write to `.tmp` files, flush to disk, and atomically replace target files (`File.Move(tmp, target, overwrite: true)`). Corrupted files are safely backed up to `.corrupt.<timestamp>`.
- **Configuration Validation (`AppSettings.cs`)**: `Validate()` sanitizes and bounds port numbers (1-65535), queue depth (1-100), max image size (1-50 MB), file transfer size caps, and history retention.
- **IPC & System Tray Shell**: Single-instance mutex combined with a background `NamedPipeServerStream` (`ClipboardSync_IPC_Pipe`) to handle CLI/secondary launch actions (`OPEN`, `SHOW_PEERS`, `EXIT`). Standard 8-step graceful shutdown with a 5.0-second hard timeout.

---

## 4. Verification & Test Suite

The application includes an automated test suite (`ClipboardSync.Tests`) covering core contracts and failure paths.

### Test Results:
```text
Test Run Successful.
Total tests: 12
     Passed: 12
 Total time: 1.7487 Seconds
```

| Test Case | Status | Subsystem Tested |
| --- | --- | --- |
| `BoundedLruCache_Eviction` | Passed | LRU Eviction & Capacity Caps |
| `FrameCipher_EncryptAndDecrypt` | Passed | 14B AAD AES-GCM Framing & Sequence Nonces |
| `FrameCipher_ProtectAndUnprotectSecret` | Passed | DPAPI Key Protection |
| `PayloadQueue_Enqueue` | Passed | Text Coalescing & State Isolation |
| `FileTransferService_PathSanitization` | Passed | Path Traversal Sanitization |
| `NamedPipeIpc_ShouldExchangeCommand` | Passed | Single-Instance IPC Server |
| `AppSettings_Validate` | Passed | Configuration Bounds & Sanitization |
| `DeviceIdentity_GetOrCreatePeerId` | Passed | Persistent Installation PeerId |
| `ConnectionStore_AtomicPersistence` | Passed | Atomic Storage & Recovery |
| `DiscoveryService_ParseTailnetOutput` | Passed | Tailscale Status JSON Parsing |
| `TransferQueue_Enqueue` | Passed | Queue Depth Caps |
| `Start_WhenPortInUse` | Passed | Graceful Port Binding Error Handling |

---

## 5. Build & Distribution

- **Framework**: .NET 8.0 Windows (`net8.0-windows`)
- **Build Output**: `C:\My_projects\ClipboardSync\publish\ClipboardSyncApp.exe`
- **Publish Command**:
  ```powershell
  dotnet publish ClipboardSyncApp\ClipboardSyncApp.csproj -c Release -o publish
  ```
- **Automated Script**: `setup.ps1` restores dependencies, compiles, and publishes the Release binary bundle.
