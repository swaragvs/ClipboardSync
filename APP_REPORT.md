# ClipboardSync App Report

## 1. Overview
ClipboardSync is a lightweight Windows desktop application designed to mirror text copied on one trusted device to another over a private Tailscale network. It is built as a .NET 8 WinForms application and uses a simple TCP listener and client connection model for clipboard synchronization.

The project is described in [README.md](README.md) and implemented in [ClipboardSyncApp/ClipboardSyncApp.csproj](ClipboardSyncApp/ClipboardSyncApp.csproj) and [ClipboardSyncApp/Form1.cs](ClipboardSyncApp/Form1.cs).

## 2. Functional Features
- Text clipboard synchronization between computers on the same Tailscale network
- Local listener running on a configurable TCP port
- Manual peer configuration using a Tailscale IP address and port
- Connection testing button to validate peer reachability
- Send test message feature for quick communication checks
- Clipboard monitoring using Windows clipboard change notifications
- Remote clipboard injection with message suppression to avoid loops
- Duplicate message prevention using a session ID and message ID
- Simple status log for connection, sending, receiving, and error messages
- Designed for private, trusted device-to-device use

## 3. Core Behavior
The app listens for incoming TCP connections on the configured port and reads JSON payloads containing:
- SessionId
- MessageId
- Text

When the local clipboard changes, it sends the text to the configured peer. When a remote payload is received, it places the text back into the local clipboard and suppresses a local echo to avoid recursive updates.

## 4. Tech Stack and Versions
### Runtime / Framework
- .NET SDK target: net8.0-windows
- Language: C#
- UI framework: Windows Forms (WinForms)
- Platform: Windows 10 or later

### Project file configuration
From [ClipboardSyncApp/ClipboardSyncApp.csproj](ClipboardSyncApp/ClipboardSyncApp.csproj):
- TargetFramework: net8.0-windows
- OutputType: WinExe
- Nullable: enabled
- UseWindowsForms: true
- ImplicitUsings: enabled

### Build and publish tooling
- Setup script uses .NET 8 SDK detection and installation via winget
- Publish is configured toward a Windows target, with support for self-contained builds
- The project is built and published using standard .NET CLI commands

### Networking and OS integration
- TCP socket communication using System.Net.Sockets
- Clipboard monitoring via WM_CLIPBOARDUPDATE and user32.dll listener APIs
- JSON serialization via System.Text.Json

### Dependency status
This project currently has no NuGet package references beyond the standard .NET runtime libraries. The app relies primarily on the built-in .NET and Windows APIs rather than third-party libraries.

## 5. Limitations and Risks
- Text-only clipboard sync; no support for images, files, or rich clipboard formats
- Intended only for trusted local/private networks; not a general-purpose cloud sync tool
- Requires Tailscale to be installed and connected on both devices
- Requires the peer device to be reachable via its Tailscale IP and open port
- No end-user authentication or authorization beyond network trust assumptions
- No encryption beyond Tailscale network privacy; the app does not implement its own TLS layer for payloads
- No file transfer, multi-device fan-out, or queue management
- No persistence of clipboard history beyond the current session state
- No UI validation for advanced edge cases beyond basic IP and port checks
- Message handling is local-process oriented and may not scale well in more complex multi-peer scenarios
- The app is still under active development and may change over time

## 6. Security and Privacy Notes
The application is designed for direct communication between trusted devices on a private Tailscale network. Clipboard content is intentionally transmitted between configured endpoints. This approach is useful for private sync, but users should be careful with sensitive clipboard data and must trust both devices and the network path.

## 7. Operational Requirements
- Windows machine with .NET 8 SDK for development/build
- Tailscale installation and same account/network on both devices
- Valid peer Tailscale IP and target port
- TCP port availability on the local machine
- Application running on both endpoints for synchronization

## 8. Summary
ClipboardSync is a compact Windows clipboard-sharing utility built for private, trusted peer-to-peer syncing over Tailscale. It is simple, practical, and focused on a narrow use case: live text clipboard synchronization on a private network. Its main strengths are simplicity and low overhead, while its main weaknesses are limited feature scope, lack of advanced security controls, and its dependence on a trusted Tailscale environment.
