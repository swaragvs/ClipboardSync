# ClipboardSync

A lightweight Windows clipboard synchronization tool for securely sharing clipboard text between trusted devices over a Tailscale network.

## What it does

- Watches the Windows clipboard for text changes
- Sends new clipboard content to a configured peer IP over TCP
- Receives clipboard updates from another machine
- Prevents feedback loops by suppressing echo-back of the same message
- Runs as a simple WinForms app with a status log

## Requirements

- Windows 10 or later
- .NET 8 SDK
- Tailscale installed and connected on both machines
- Both devices reachable over Tailscale (`100.x.x.x` addresses)

## Project structure

- `ClipboardSyncApp/` - Windows app source code
- `.github/workflows/build.yml` - GitHub Actions CI workflow

## Run locally

1. Open a terminal in the project root.
2. Restore dependencies:

   ```powershell
   dotnet restore ClipboardSyncApp/ClipboardSyncApp.csproj
   ```

3. Run the app:

   ```powershell
   dotnet run --project ClipboardSyncApp/ClipboardSyncApp.csproj
   ```

4. In the UI:
   - Enter the peer machine's Tailscale IP, for example `100.64.0.2`
   - Keep the port as `5001` or change it if needed
   - Click `Connect` to test the connection
   - Copy text on one machine and it should appear on the other

## Important notes

- This is designed for a trusted local network over Tailscale.
- The app currently supports plain text clipboard sync.
- You should use a fixed peer IP and port for reliable communication.
- The app uses a message ID to avoid infinite resend loops.

## Build manually

```powershell
dotnet build ClipboardSyncApp/ClipboardSyncApp.csproj -c Release
```

## Publish manually

```powershell
dotnet publish ClipboardSyncApp/ClipboardSyncApp.csproj -c Release -o ./publish
```

## GitHub Actions

The repository includes a CI workflow in `.github/workflows/build.yml` that restores, builds, and publishes the app on Windows.

## Security note

This app is intended for a private Tailscale mesh between trusted devices. It does not use a cloud service or external relay.
