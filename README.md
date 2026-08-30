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

## Getting Started
Clone the repository and restore the project:

```
dotnet restore ClipboardSyncApp/ClipboardSyncApp.csproj
```
Run the application:

```
dotnet run --project ClipboardSyncApp/ClipboardSyncApp.csproj
```
Configure the peer device in the application and connect.

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
