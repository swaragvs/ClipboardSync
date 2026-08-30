# ClipboardSync v2 — Project Specification

## 0. Context

**Current state (v1):**
- .NET 8 WinForms app, C#, `net8.0-windows`, WinExe, WinForms UI
- TCP listener/client model over Tailscale IPs
- JSON payload: `SessionId`, `MessageId`, `Text`
- Text-only sync, manual peer IP+port entry, connection test button
- No NuGet deps beyond BCL
- Runs in foreground only, no icon, no autostart, no history, dies on window close

**Target state (v2):** a proper background-capable, multi-device, rich-clipboard sync utility — still scoped to trusted Tailscale peers, still no cloud component, still simple to audit.

**Non-negotiable constraints carried forward from v1:**
- No cloud relay. Peer-to-peer over Tailscale only.
- Keep dependency footprint minimal — prefer BCL / a small number of well-known NuGet packages over pulling in a framework.
- Every phase must leave the app in a *runnable, testable* state. Don't let a phase half-finish a subsystem.

---

## 1. Target Architecture

Move off a single `Form1.cs` god-file. New project layout:

```
ClipboardSyncApp/
├── ClipboardSyncApp.csproj
├── App.manifest / app.ico
├── Program.cs                     # entry point, single-instance check, tray bootstrap
├── Core/
│   ├── ClipboardWatcher.cs        # WM_CLIPBOARDUPDATE wrapper, format-aware
│   ├── ClipboardPayload.cs        # models: Text / Image / File payload types
│   ├── PeerConnection.cs          # single TCP session (send/recv, framing, retry)
│   ├── PeerManager.cs             # owns N PeerConnections, fan-out, health checks
│   ├── DiscoveryService.cs        # Tailscale peer auto-identify (Phase 2)
│   ├── TransferQueue.cs           # outbound queue, chunking, backpressure (Phase 4)
│   └── Security/
│       ├── HandshakeService.cs    # peer auth handshake (Phase 6)
│       └── FrameCipher.cs         # payload encryption (Phase 6)
├── Storage/
│   ├── ConnectionProfile.cs       # saved peer: name, IP, port, last-seen, trust flag
│   ├── ConnectionStore.cs         # JSON/SQLite persistence for profiles + history
│   └── ClipboardHistoryStore.cs   # persisted clipboard history (Phase 5)
├── UI/
│   ├── TrayContext.cs             # NotifyIcon, context menu, lifecycle glue
│   ├── MainForm.cs                # visible settings/status window (was Form1)
│   ├── HistoryForm.cs             # clipboard history browser (Phase 5)
│   └── PeerManagerForm.cs         # saved connections / peer list UI (Phase 2)
├── Config/
│   └── AppSettings.cs             # autostart, close-behavior, port, autoconnect list
└── Resources/
    └── app.ico
```

**Key architectural decisions:**
- **Separate "engine" from "UI".** `Core/` + `Storage/` must run with zero WinForms references — `MainForm` and `TrayContext` are just views over the engine. This is what makes "run headless without GUI" possible later without a rewrite.
- **NotifyIcon-based tray shell** replaces "app = one form." `Program.cs` starts the engine + tray unconditionally; `MainForm` is opened/closed on demand and closing it never kills the process.
- **Single-instance enforcement** via a named `Mutex`, so double-launching doesn't spawn a second listener on the same port.
- **Payload framing gets a type tag** from Phase 3 onward: `{ Kind: "text"|"file"|"image", SessionId, MessageId, ... }` — design the wire format for this in Phase 1 even if only "text" is implemented, so you don't break wire compatibility later.

---

## 2. Phase Plan

Each phase below is written as a **standalone build prompt** — paste the "Prompt for builder" block into Claude Code (or whatever you're using) as-is, one phase at a time, after the previous phase is merged and tested.

---

### Phase 1 — Lifecycle & Shell (tray, autostart, background, close behavior)

**Goal:** Turn the app from "a form that dies when closed" into a proper background service with a tray presence — no new sync features yet, just the shell.

**Scope:**
- App icon (`.ico`) applied to exe, tray icon, and taskbar
- `NotifyIcon` in system tray with context menu: Open, Pause Sync, Settings, Exit
- Closing `MainForm` (X button) minimizes to tray, does **not** terminate the process
- Explicit "Exit" in tray menu is the *only* way to actually terminate
- True background/headless mode: app can run with `--background` / a settings flag with no window shown at all on launch (just tray icon)
- Autostart on Windows login (registry `Run` key or Startup folder shortcut — toggle in settings, off by default)
- Single-instance guard (Mutex) — second launch just focuses/restores the existing instance instead of starting a second listener
- Refactor `Form1.cs` logic into `Core/` (headless-safe) vs `UI/MainForm.cs` (view only), per the architecture above

**Explicitly out of scope for this phase:** peer discovery, history, file/image support, encryption. Behavior of the sync engine itself should be unchanged from v1 — this phase is pure shell/lifecycle.

**Deliverables:**
- `TrayContext.cs`, refactored `Program.cs`, `Config/AppSettings.cs` (persisted to a simple JSON settings file in `%AppData%`)
- Settings toggle: "Start with Windows", "Start minimized to tray", "Close button minimizes instead of exits"
- Manual test checklist in the PR description: launch → tray icon appears → close window → process still alive → reopen from tray → exit from tray → process gone

**Prompt for builder:**
> Refactor this WinForms clipboard sync app so it runs as a tray-resident background app instead of a single foreground form. Extract the existing TCP listener/sender logic out of Form1.cs into a UI-agnostic `Core` namespace with no WinForms dependencies. Add a `NotifyIcon`-based tray shell (`TrayContext.cs`) with Open/Pause/Settings/Exit menu items. Add a `Program.cs` that: (1) enforces single-instance via a named Mutex, (2) starts the tray + engine on launch, (3) optionally starts with the main window hidden if a `--background` flag or a persisted "start minimized" setting is set. Add `Config/AppSettings.cs` backed by a JSON file in `%AppData%\ClipboardSync\settings.json` with fields for `StartWithWindows`, `StartMinimized`, `CloseToTray`. Implement "Start with Windows" via a Startup-folder shortcut (not a raw registry write, for easier uninstall). Make the main form's close button (and the top-right X) minimize-to-tray instead of exiting the process; only the tray "Exit" menu item should truly shut down. Apply an app icon (I'll supply `app.ico`, use a placeholder if absent) to the exe, taskbar, and tray. Keep the actual clipboard-sync wire protocol and TCP behavior identical to the current implementation — this phase is lifecycle/shell only, not sync logic.

---

### Phase 2 — Connection Management (saved profiles, history, autoconnect, auto-identify)

**Goal:** Stop retyping IPs. Remember peers, reconnect automatically, and (where feasible) discover them.

**Scope:**
- `Storage/ConnectionProfile.cs` + `ConnectionStore.cs`: persist named peer profiles (`Name`, `TailscaleIp`, `Port`, `LastConnectedUtc`, `AutoConnect: bool`)
- `UI/PeerManagerForm.cs`: list of saved peers, add/edit/remove, "connect now" button, marks which are currently online (last successful handshake)
- Recent-connections history: every successful connection appends/updates an entry (not just manual saves) so accidental one-off connections are still recoverable
- **Autoconnect:** on launch, attempt to connect to every profile flagged `AutoConnect`, with retry/backoff (e.g. 5s, 15s, 60s, then poll every 60s) — must not block the UI thread
- **Auto-identify:** best-effort peer discovery on the Tailscale network:
  - Primary approach: shell out to `tailscale status --json` (if the Tailscale CLI is present) to enumerate peer hostnames/IPs on the same tailnet, and present them as "discovered" candidates the user can promote to a saved profile with one click
  - Fallback if Tailscale CLI isn't accessible: a lightweight UDP broadcast/announce on the Tailscale interface so ClipboardSync instances can find each other by app-level handshake (only used as fallback — don't require this if `tailscale status` works)
- `PeerConnection` gains automatic reconnect-on-drop for any profile marked `AutoConnect`

**Deliverables:**
- Saved-peers JSON store (`%AppData%\ClipboardSync\peers.json`)
- `PeerManagerForm` reachable from tray menu and main window
- Reconnect/backoff logic covered by at least a manual test: kill peer app, confirm this instance keeps retrying without spamming logs or the UI

**Prompt for builder:**
> Add persistent connection management to the ClipboardSync engine. Create `Storage/ConnectionProfile.cs` (Name, TailscaleIp, Port, LastConnectedUtc, AutoConnect) and `Storage/ConnectionStore.cs` for JSON persistence in `%AppData%\ClipboardSync\peers.json`. Build `UI/PeerManagerForm.cs` listing saved peers with add/edit/remove/connect actions and a live online/offline indicator per peer. Every successful connection (manual or auto) should update or create a history entry automatically, not just explicit saves. Implement autoconnect: on startup, attempt connections to all `AutoConnect`-flagged profiles with exponential backoff (5s/15s/60s, then steady 60s polling) on background tasks that never block the UI thread; auto-reconnect any dropped autoconnect peer using the same backoff. Implement auto-identify/discovery: attempt to shell out to `tailscale status --json`, parse peer hostnames and Tailscale IPs, and surface them in the PeerManagerForm as "Discovered" entries the user can promote to a saved profile in one click; if the Tailscale CLI isn't found on PATH, degrade gracefully (discovery section just shows "Tailscale CLI not found — add peers manually") rather than crashing or blocking. Wire the tray "Open" menu to include a shortcut to Peer Manager.

---

### Phase 3 — Rich Clipboard Support (images, files, rich formats)

**Goal:** Stop being text-only.

**Scope:**
- Extend `ClipboardWatcher` to detect clipboard format on change: `CF_TEXT/Unicode`, `CF_BITMAP/DIB` (images), `CF_HDROP` (file paths), and optionally `CF_RTF`/HTML clipboard format
- Extend wire payload to a tagged union: `{ Kind: "text"|"image"|"file-ref"|"rtf", SessionId, MessageId, ... }` (this is the schema you should have stubbed in Phase 1)
- Image payloads: serialize as PNG bytes (base64 or raw over the socket — raw preferred, see framing note below), size-cap with a configurable max (e.g. 25MB) and reject/log oversized images rather than hanging the connection
- File payloads in this phase = **file *paths/metadata* only** (name, size) with a "Peer wants to send file X — Accept?" prompt; actual file *bytes* transfer is Phase 4. This phase just gets clipboard file-reference detection and the payload schema working.
- Rich text (RTF/HTML): pass through as a string payload kind, same suppression-on-echo logic as text

**Technical note — framing:** since payloads are no longer small JSON text blobs, switch the socket protocol to length-prefixed framing (4-byte big-endian length + payload bytes) instead of relying on line/JSON boundary parsing. Do this framing change first, as an isolated sub-step, before adding image support, since it changes the wire protocol for *all* payload kinds including plain text.

**Deliverables:**
- Length-prefixed framing in `PeerConnection` (breaking wire change — bump a protocol version byte at the start of the frame so mismatched versions fail loudly instead of silently corrupting)
- Image round-trip: copy an image on machine A, appears on machine B's clipboard
- File-reference round-trip: copy a file in Explorer on A, B sees a toast/log "peer offered file: report.pdf (2.3MB)" (no transfer yet)

**Prompt for builder:**
> Extend ClipboardSync's payload protocol to support more than plain text. First, change the wire protocol from ad-hoc JSON-over-socket to length-prefixed framing: each frame is a 1-byte protocol version + 4-byte big-endian payload length + payload bytes, so we can safely send binary data and detect version mismatches. Then extend `ClipboardPayload` to a tagged union with a `Kind` field: `"text"`, `"image"`, `"file-ref"`, `"rtf"`. Update `ClipboardWatcher` to detect which clipboard format changed (text, DIB/bitmap, HDROP file drop list, RTF) and construct the matching payload type. For images: serialize as PNG bytes, enforce a configurable max size (default 25MB) with graceful rejection (log + skip, don't crash or hang) above that. For file references: only send name+size+a local path token in this phase (no bytes yet) and show a UI/tray notification on the receiving side ("Peer offered file: report.pdf, 2.3MB") — actual file transfer is a future phase, don't implement it here. For RTF/HTML: treat as a string payload with the same remote-injection + echo-suppression logic already used for plain text. Keep all of this behind the existing `PeerConnection`/`PeerManager` abstractions from Phase 1/2 — this phase changes what's inside a frame, not how peers connect.

---

### Phase 4 — File Transfer, Multi-Device Fan-Out, Queue Management

**Goal:** Actually send file bytes, to more than one peer, without one slow peer blocking the others.

**Scope:**
- Complete the file-ref flow from Phase 3: on accept, stream the actual file bytes to the requesting peer in chunks (e.g. 64KB chunks) over the same length-prefixed framing, with a simple progress event the UI can show
- `Storage`-side temp handling: incoming files land in a configurable "ClipboardSync Received" folder, not directly onto the live clipboard until fully received and verified (checksum)
- **Multi-device fan-out:** `PeerManager` sends every outbound clipboard change to *all* currently-connected `AutoConnect`/active peers, not just one — each peer connection is independent so one slow/stalled peer doesn't block delivery to the others (use per-peer send queues, not a shared blocking call)
- **Queue management:** per-peer outbound queue with:
  - Coalescing: if the clipboard changes again before a queued text send completes, drop the stale one (don't flood on rapid copy/paste) — but never coalesce in-flight file transfers, let those finish or fail explicitly
  - Basic backpressure: cap queue depth per peer; if exceeded, drop oldest queued *text* item and log it (files should generally not be silently dropped — surface a failure instead)
- Cancel-in-progress-transfer support (user can cancel a large file send from the UI)

**Deliverables:**
- File transfer round-trip with progress bar, checksum-verified
- Fan-out test: 3 running instances, confirm a clipboard change on one reaches both others independently, and one peer being paused/unreachable doesn't stall delivery to the third
- Queue depth cap is configurable in settings, sane default (e.g. 20 pending text items)

**Prompt for builder:**
> Implement real file transfer and multi-peer fan-out for ClipboardSync, building on the file-ref payload type from the previous phase. On file-ref accept, stream the file in 64KB chunks over the existing length-prefixed frame protocol, writing to a configurable "Received" folder and verifying a checksum (e.g. SHA-256) before considering the transfer complete; expose a progress event/callback the UI can bind a progress bar to, and support cancellation mid-transfer. Change `PeerManager` so outbound clipboard events fan out independently to every currently-connected peer — give each `PeerConnection` its own outbound send queue/worker so a stalled or slow peer cannot block delivery to other peers. Implement queue coalescing for plain-text payloads (a new clipboard text change should replace, not queue behind, a not-yet-sent previous text payload) but never coalesce or drop an in-progress or queued file transfer — those should complete or fail explicitly with a surfaced error. Add a configurable max queue depth per peer (default 20) for non-file payloads; when exceeded, drop the oldest queued text item and log it rather than growing unbounded.

---

### Phase 5 — Clipboard History Persistence

**Goal:** Clipboard history survives restarts and is browsable.

**Scope:**
- `Storage/ClipboardHistoryStore.cs`: append-only local store (SQLite recommended over flat JSON once you're storing images/files — use `Microsoft.Data.Sqlite`, the one NuGet dependency worth adding here) of: timestamp, source peer (local vs which remote), kind, text/preview, and for images/files a pointer to a cached copy on disk
- Configurable retention: max item count and/or max age (e.g. keep last 200 items or 30 days), with a manual "Clear History" action
- `UI/HistoryForm.cs`: searchable/filterable list (by text content, by peer, by date), click an entry to re-copy it to the live clipboard
- Sensitive-content awareness: add a simple "Pause history" / "exclude this app" toggle so the user can stop sensitive copies (e.g. from a password manager) from being persisted — check the clipboard's format for a no-history marker (`ExcludeClipboardContentFromMonitorProcessing` format, which some password managers already set) and respect it automatically

**Deliverables:**
- History persists across app restart
- Respect the standard "exclude from clipboard history" format flag that Windows itself defines, so password managers that already set it are automatically excluded
- Retention policy enforced (old entries pruned) without needing a manual trigger

**Prompt for builder:**
> Add persistent, searchable clipboard history to ClipboardSync. Add a `Microsoft.Data.Sqlite` dependency and create `Storage/ClipboardHistoryStore.cs` storing every processed clipboard payload (local and remote) with timestamp, source (local or which peer), kind, a text preview or a cached-file path for images/files, in a SQLite DB under `%AppData%\ClipboardSync\history.db`. Add configurable retention (max item count default 200, max age default 30 days) enforced automatically on a background timer, plus a manual "Clear History" action. Build `UI/HistoryForm.cs` with search/filter by text, peer, and date range, and a "re-copy to clipboard" action per entry, reachable from the tray menu. Respect the Windows-standard clipboard "exclude from history/cloud clipboard" format (`CFSTR_EXCLUDEFROMCLOUDCLIPBOARD` / `ExcludeClipboardContentFromMonitorProcessing`, the format some password managers set) by checking for it in `ClipboardWatcher` and skipping persistence (but still allowing live sync if the user wants that separately — history exclusion and sync are different toggles). Add a global "Pause history" toggle in settings that stops writes to the history store without stopping live sync.

---

### Phase 6 — Security Hardening

**Goal:** Stop relying entirely on "well, it's on Tailscale so it's fine."

**Scope:**
- Peer authentication handshake: shared pre-shared key (PSK) configured per saved profile, or a simple pairing flow (show a short code on both instances, confirm on both sides) before a connection is treated as trusted
- Payload encryption: AES-GCM (via `System.Security.Cryptography`, no extra dependency needed) over each frame's payload, keyed from the paired session, so plaintext clipboard content never sits on the wire even inside the private tailnet
- Reject/log any peer that fails handshake instead of silently accepting

**Deliverables:**
- Two instances can only sync after an explicit pairing step
- Wire capture (e.g. Wireshark on loopback/LAN) shows encrypted bytes, not plaintext clipboard content
- Document the threat model explicitly in README: this still assumes Tailscale-level network trust; the added crypto defends against payload inspection and accidental cross-talk, not a compromised tailnet peer

**Prompt for builder:**
> Add peer authentication and payload encryption to ClipboardSync. Implement a pairing flow: when adding a new peer profile, generate a random pre-shared key (PSK), display a short human-verifiable code derived from it on both instances, and require the user to confirm the same code on both sides before the profile is marked trusted. Store the PSK per-profile in the existing `ConnectionProfile`. Use the PSK to derive a session key (e.g. HKDF) and encrypt every payload's bytes with AES-GCM before sending, decrypting on receipt, using `System.Security.Cryptography` only (no new NuGet dependency needed for this). Any peer that fails to complete the handshake or whose frames fail to decrypt should be rejected and logged, never silently passed through. Update the README's security section to describe the new threat model explicitly: encryption protects payload confidentiality/integrity on the wire; it does not replace Tailscale-level network trust, and a compromised paired peer is still fully trusted.

---

### Phase 7 — Polish & Distribution

**Goal:** Make it feel like a finished product.

**Scope:**
- Windows toast notifications (via `Microsoft.Toolkit.Uwp.Notifications` or the WinRT notification APIs) for: file received, peer connected/disconnected, sync paused
- Proper installer (MSIX or a simple Inno Setup script) instead of manual `dotnet publish` copy
- Auto-update check (simple: compare a version string against a GitHub Releases API, prompt to download — no auto-patching needed)
- Settings UI consolidation: one tabbed settings dialog (General / Peers / History / Security) instead of scattered forms
- Logging cleanup: rotate/cap log file size, add a "View Logs" button in the tray/settings for support purposes

**Prompt for builder:**
> Polish ClipboardSync for distribution. Add Windows toast notifications for key events (file received, peer connected, peer disconnected, sync paused) using the WinRT notification APIs. Consolidate the scattered settings/peer/history forms into a single tabbed settings dialog (General, Peers, History, Security tabs) without changing the underlying storage classes. Add a lightweight update check on startup: fetch the latest release tag from the project's GitHub Releases API and show a non-blocking "update available" notification if the running version is older (no auto-install, just notify + link). Cap the existing log file at a configurable size (default 5MB) with rotation, and add a "View Logs" / "Open Log Folder" action in the tray menu. Produce an Inno Setup script (or MSIX manifest, your call — document the choice) that packages the published build into a proper installer with Start Menu shortcut and optional "run at startup" checkbox during install.

---

## 3. Suggested Build Order Recap

| Phase | Theme | Depends on |
|---|---|---|
| 1 | Tray shell, autostart, background, single-instance | — |
| 2 | Saved peers, autoconnect, auto-identify, history-of-connections | 1 |
| 3 | Framing rework + images/files/rich-text payload types | 1 |
| 4 | Real file transfer, multi-peer fan-out, queues | 2, 3 |
| 5 | Persistent clipboard history + browser UI | 3 |
| 6 | Auth + encryption | 2, 3 |
| 7 | Notifications, installer, settings consolidation | all |

Phases 3 and 5 can be developed somewhat in parallel with Phase 2 if you want to parallelize, since they touch different subsystems (payload/wire vs. peer bookkeeping) — but do Phase 1 first regardless, since it's the shell everything else hangs off of.

## 4. How to Use This Doc

Feed one "Prompt for builder" block at a time to your coding assistant, in order. After each phase:
1. Build and run manually against the checklist in that phase's Deliverables.
2. Commit/tag before starting the next phase.
3. If a phase reveals the architecture doc needs adjusting (e.g. framing decision changes), update Section 1 before moving on — don't let the doc drift from reality.