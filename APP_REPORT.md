# ClipboardSync App Report

## 1. Overview
ClipboardSync is a lightweight Windows desktop application for mirroring text copied on one trusted device to another over a private Tailscale network. It is a .NET 8 WinForms app that runs as a tray-resident background service and keeps the original peer-to-peer clipboard sync model intact.

The current implementation is organized around a UI shell and a reusable core engine, with the main startup and lifecycle logic in [ClipboardSyncApp/Program.cs](ClipboardSyncApp/Program.cs), [ClipboardSyncApp/Form1.cs](ClipboardSyncApp/Form1.cs), [ClipboardSyncApp/Core/ClipboardSyncEngine.cs](ClipboardSyncApp/Core/ClipboardSyncEngine.cs), and [ClipboardSyncApp/UI/TrayContext.cs](ClipboardSyncApp/UI/TrayContext.cs).

## 2. Current Functional Features
- Text clipboard synchronization between trusted Windows machines on the same Tailscale network
- Local listener bound to a configurable TCP port
- Manual peer IP and port configuration in the main form
- Connection test button for peer reachability validation
- Send Test button for quick communication checks
- Clipboard change detection using Windows clipboard format listener APIs
- Remote clipboard injection with local echo suppression to avoid feedback loops
- Duplicate message prevention using per-message IDs and local instance IDs
- Status logging for send, receive, connection, and error events
- Tray-based app shell with Open / Pause / Settings / Exit actions
- Single-instance startup protection to avoid duplicate listeners
- Background startup mode with `--background` or minimized startup configuration
- Close-to-tray behavior so the app keeps running after the form is closed
- Startup-folder shortcut support for “Start with Windows” via persisted settings
- Graceful handling of port conflicts instead of crashing when the bound port is unavailable

## 3. Current Runtime Architecture
The app has been refactored into a clearer structure:
- Core engine: clipboard sync, listener loop, send/receive logic
- UI shell: main form and tray context only
- Config layer: persisted AppData settings for startup and tray behavior

The engine still uses the original simple protocol model:
- TCP listener on the local port
- plain text JSON payload carrying SessionId, MessageId, and Text
- direct peer-to-peer sync over Tailscale IPs

## 4. Tech Stack and Versions
### Runtime / Framework
- .NET target: net8.0-windows
- C# language version: default for .NET 8
- UI framework: Windows Forms (WinForms)
- Platform: Windows 10+

### Project configuration
From [ClipboardSyncApp/ClipboardSyncApp.csproj](ClipboardSyncApp/ClipboardSyncApp.csproj):
- TargetFramework: net8.0-windows
- OutputType: WinExe
- Nullable: enabled
- UseWindowsForms: true
- ImplicitUsings: enabled

### Dependency status
- No heavy third-party dependency stack
- Uses standard .NET runtime libraries and Windows APIs only
- Includes a small xUnit test project for regression validation

### Build and validation tooling
- Build command used successfully: `dotnet build ClipboardSyncApp/ClipboardSyncApp.csproj -c Release`
- Regression test command used successfully: `dotnet test ClipboardSync.Tests/ClipboardSync.Tests.csproj --no-restore`

### Networking and OS integration
- TCP socket communication via `System.Net.Sockets`
- Clipboard monitoring via `WM_CLIPBOARDUPDATE` and `user32.dll` listener APIs
- JSON serialization via `System.Text.Json`
- Tray shell via `NotifyIcon` and WinForms context menus
- AppData persistence via `Environment.SpecialFolder.ApplicationData`

## 5. Current Limitations and Risks
- Text-only clipboard sync; image, file, and rich-format clipboard support are not yet implemented
- Requires Tailscale to be installed and connected on both devices
- Requires the peer device to be reachable through its Tailscale IP and the configured port
- No authentication or authorization beyond trusted network assumptions
- No encryption layer beyond the trust of the Tailscale private network
- No persistent clipboard history or advanced file transfer flow yet
- No multi-peer fan-out or queue management beyond the simple direct sync pattern
- No advanced recovery logic when the chosen port is busy; the app now reports the condition without crashing, but it still requires the user to change ports or stop the conflicting process
- No user-facing settings UI beyond the basic tray/startup settings already wired in
- This is still a focused, early-stage product with a clear path toward more advanced features

## 6. Security and Privacy Notes
ClipboardSync is intended for trusted, private device-to-device communication over a Tailscale network. The app currently transmits clipboard content directly between configured peers and assumes both the network and the devices are trusted.

This means it is suitable for private synchronization within a trusted environment, but it is not a hardened security product. Sensitive text should be handled carefully, and users should be aware that the app does not yet include peer authentication or payload encryption.

## 7. Operational Requirements
- Windows machine with the .NET 8 SDK for development/build
- Tailscale installed and connected on both devices
- Same Tailscale network / account on both endpoints
- Valid Tailscale peer IP and port
- TCP port availability on the local machine
- App running on both devices for sync to function

## 8. Phase 1 Status Summary
Phase 1 has been implemented and validated. The app now behaves as a tray-resident background utility rather than a single foreground form, while preserving the original sync workflow.

This includes:
- single-instance startup guard
- tray menu with Open / Pause / Exit flow
- background or minimized launch behavior
- startup-folder integration for automatic launch on login
- close-to-tray behavior instead of process termination on window close
- graceful port conflict handling to avoid the crash seen with `SocketException (10048)`

## 9. Summary
ClipboardSync is now a more practical desktop utility for private, trusted clipboard sharing over Tailscale. It retains the original simple operating model but adds the lifecycle and background-shell improvements required for real-world use on Windows. The app remains intentionally narrow in scope, with future phases focused on saved peers, history, richer clipboard formats, file transfer, and stronger security.
