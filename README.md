# ClipboardSync

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6)](#requirements)
[![License](https://img.shields.io/badge/license-see%20LICENSE-lightgrey)](#license)

A lightweight Windows application for **peer-to-peer clipboard synchronization** between devices.

ClipboardSync allows connected devices to share clipboard content directly with each other, keeping copied text and images synchronized while the application runs quietly in the background.

## Table of Contents

- [Features](#features)
- [Clipboard Support](#clipboard-support)
- [Requirements](#requirements)
- [Installation](#installation)
- [Build From Source](#build-from-source)
- [Publishing](#publishing)
- [Running After Publishing](#running-after-publishing)
- [Network Setup](#network-setup)
- [Verification](#verification)
- [Project Status](#project-status)
- [Roadmap](#roadmap)
- [License](#license)

## Features

### Peer-to-Peer Clipboard Synchronization

- Synchronizes clipboard content directly between connected peers.
- No central clipboard server is required.
- A clipboard update on one device can be propagated to the connected peer.
- Supports two-way clipboard synchronization.
- Designed to work across devices connected through private networks such as Tailscale.
- Configurable peer IP address/hostname and port.

### Text Clipboard Support

- Supports copying and synchronizing text between devices.
- Detects clipboard changes automatically.
- Remote clipboard text can be applied to the local clipboard.
- Prevents received clipboard content from creating an endless synchronization loop.

### Image Clipboard Support

- Supports clipboard images.
- Images copied on one device can be transferred to the connected peer and placed into its clipboard.
- Enables convenient copying of screenshots and other clipboard-compatible image content between devices.

### Background Operation

- Runs in the background after startup.
- Continuously monitors the clipboard for changes.
- Synchronization happens without requiring the application window to remain open.
- Designed to stay out of the way during normal desktop usage.

### Peer Connection Management

- Maintains connections with configured peers.
- Displays peer connection status.
- Handles connection and disconnection events.
- Attempts to reconnect when a peer becomes unavailable.
- Reports connection and transport errors through application logs.

### Activity Logging

Provides useful runtime information including:

- Clipboard updates sent to peers.
- Clipboard updates received from peers.
- Peer connection status.
- Connection attempts.
- Disconnections.
- Network/transport errors.

### Synchronization Loop Protection

ClipboardSync includes protection against the common synchronization feedback-loop problem, for example:

```text
Device A → Device B → Device A → Device B → ...
```

Received clipboard content is not blindly treated as a new local clipboard change, preventing the same content from continuously bouncing between peers.

## Clipboard Support

| Content Type | Status              |
| ------------ | ------------------- |
| Text         | ✅ Supported         |
| Images       | ✅ Supported         |
| Files        | ❌ Not yet supported |

**File transfer is not currently implemented.** ClipboardSync currently synchronizes clipboard text and images, but copying files through the Windows clipboard is not yet transferred between peers. File synchronization/transfer can be added in a future release.

## Requirements

- Windows
- .NET 8 SDK/runtime
- Network connectivity between the devices
- A configured peer running ClipboardSync
- Appropriate firewall/network access for the configured port

For development/building from source, install the **.NET 8 SDK**.

## Installation

### Option 1 — Use the Published Build

Recommended for normal end-user installation. The .NET SDK is not required when using a self-contained published build.

1. Download the latest ClipboardSync release.
2. Download the published `.zip` package from the **Release Assets**.
3. Extract the ZIP file to a folder.
4. Open the extracted folder.
5. Run `ClipboardSyncApp.exe`.
6. Configure the peer/device connection.
7. Run ClipboardSync on the other device.
8. Once the peers connect, clipboard synchronization can begin.

## Build From Source

```bash
git clone https://github.com/swaragvs/ClipboardSync.git
cd ClipboardSync/ClipboardSyncApp
dotnet restore
dotnet build
dotnet run
```

## Publishing

To create a release-ready build:

```bash
dotnet publish -c Release
```

The published application will be placed under the project's `bin` directory, inside the Release publish folder, for example:

```text
ClipboardSyncApp/
└── bin/
    └── Release/
        └── net8.0-windows/
            └── publish/
```

The exact output path can vary depending on the project configuration.

### Recommended Windows Publish

For easier distribution to another Windows machine, create a self-contained Windows build:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

This produces a Windows x64 build that includes the required .NET runtime. The resulting files can be packaged into a ZIP and uploaded to the GitHub Release.

## Running After Publishing

1. Open the `publish` folder.
2. Locate `ClipboardSyncApp.exe`.
3. Run the executable.
4. Configure the peer connection.
5. Start ClipboardSync on the second device.
6. Verify that the peers connect.
7. Copy text or an image on either device.
8. The clipboard update should be synchronized to the connected peer.

## Network Setup

ClipboardSync can be used between devices connected through a private network, for example:

```text
Device A                          Device B
ClipboardSync                     ClipboardSync
     │                                 │
     │            Peer-to-Peer         │
     └──────────► 100.x.x.x:5001 ◄─────┘
```

A private network such as Tailscale can provide connectivity between devices even when they are not on the same physical LAN. The configured port must be reachable between the two devices.

## Verification

To verify the application after building:

```bash
dotnet restore
dotnet build
dotnet test
```

A successful build should report:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

`dotnet test` can be used when test projects are present in the solution.

## Project Status

| Feature                        | Status |
| ------------------------------ | ------ |
| Peer-to-peer communication     | ✅     |
| Two-way clipboard sync         | ✅     |
| Text synchronization           | ✅     |
| Image synchronization          | ✅     |
| Background operation           | ✅     |
| Peer connection monitoring     | ✅     |
| Reconnection handling          | ✅     |
| Activity/error logging         | ✅     |
| Synchronization loop protection| ✅     |
| File transfer                  | ❌ Not implemented yet |

## Roadmap

Potential future features include:

- Clipboard file transfer
- Folder/file synchronization
- End-to-end encryption
- Multiple peer support
- Improved configuration UI
- Connection/status notifications
- Transfer statistics
- Improved logging and diagnostics
- Automatic startup with Windows

## License

See the [LICENSE](LICENSE) file for the applicable terms.

---

**ClipboardSync v1.0.0** — Simple. Direct. Peer-to-peer clipboard synchronization for Windows.
