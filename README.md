# ClipboardSync
A lightweight Windows application for synchronizing clipboard text between trusted devices over a private Tailscale network.

## Features

- Synchronizes clipboard text between connected Windows devices
- Uses direct device-to-device communication
- Works over a Tailscale network
- Lightweight Windows desktop application
- Designed for simple, private device-to-device use

## Requirements

- Windows 10 or later
- .NET 8 SDK installed on the development machine for building and running the app
- Tailscale installed and connected on both devices
- Both devices must be online and reachable through the same Tailscale network
- A known peer Tailscale IP address for the target device (for example `100.x.x.x`)
- A free TCP port on both devices for the sync listener (default: `5001`)
- A trusted local setup; this project is intended for private device-to-device clipboard sharing only
- Text clipboard use only in the current implementation; image and file clipboard sync are not included

## Quick setup on a Windows machine

This project does not use a `requirements.txt` file because it is a .NET application. The closest equivalent is a bootstrap script that installs the .NET SDK if needed, restores dependencies, builds, and publishes the app.

From PowerShell in the project root:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

This will:

- check for the .NET 8 SDK
- install it automatically if missing
- restore NuGet packages
- build the project
- publish the app to the `publish` folder

## Complete setup from clone to run

Follow these steps on each Windows machine you want to use:

1. Clone the repository:

   ```powershell
   git clone https://github.com/swaragvs/ClipboardSync.git
   cd ClipboardSync
   ```

2. Install the .NET 8 SDK if it is not already installed.

   ```powershell
   winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-source-agreements --accept-package-agreements
   ```

3. Install Tailscale and connect both devices to the same account.

4. Make sure both machines are connected to Tailscale and can see each other's Tailscale IPs.

5. Run the setup script from the repository root:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\setup.ps1
   ```

   This creates the published app in the `publish` folder.

6. Launch the app:

   ```powershell
   .\publish\ClipboardSyncApp.exe
   ```

7. In the app UI, enter the peer machine's Tailscale IP and port.

   - Example peer IP: `100.64.0.2`
   - Default port: `5001`

8. Click `Connect` to test connectivity.

9. Copy text on one device. It should appear on the other device's clipboard.

## Manual run without the setup script

If you prefer to do it manually:

```powershell
dotnet restore ClipboardSyncApp/ClipboardSyncApp.csproj
dotnet build ClipboardSyncApp/ClipboardSyncApp.csproj -c Release
dotnet run --project ClipboardSyncApp/ClipboardSyncApp.csproj
```

If you want to publish instead of running directly:

```powershell
dotnet publish ClipboardSyncApp/ClipboardSyncApp.csproj -c Release -o ./publish
```

## Getting Started

After launch, configure the peer device in the application and connect.

Once connected, clipboard text copied on one device can be synchronized to the other device.

## Build
Build a Release version with:

```
dotnet build ClipboardSyncApp/ClipboardSyncApp.csproj -c Release
```
To publish the application:

```
dotnet publish ClipboardSyncApp/ClipboardSyncApp.csproj -c Release -o ./publish
```

## Privacy
ClipboardSync is designed for direct communication between trusted devices over a private Tailscale network.

Clipboard content is transmitted between the configured devices for synchronization.

Do not use ClipboardSync with sensitive clipboard information unless you understand and trust the network and devices involved.

## Project Status
ClipboardSync is an independently developed project and is currently under active development.

Features and implementation details may change as the project evolves.

## Ownership & Usage
Copyright © 2026 Swarag V S. All rights reserved.

This repository is publicly available for viewing and reference.

No license is granted to copy, modify, redistribute, sublicense, or commercially exploit the source code unless expressly permitted by the copyright holder.

Tailscale is a separate product and trademark of its respective owner. ClipboardSync is not affiliated with or endorsed by Tailscale.

## Disclaimer
ClipboardSync is provided for personal and development use. Use it at your own discretion and ensure that it is appropriate for your environment.

## License

Copyright © 2026 Swarag V S. All rights reserved.

ClipboardSync is publicly available for viewing and personal evaluation. The source code remains the property of its author.

Permission to use, modify, redistribute, or commercially distribute the source code is not granted unless explicitly authorized by the author.
