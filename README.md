# XingShu (星枢)【吉星派对游戏助手】

<p align="center">
  <strong>Multi-client game enhancement runtime and cross-process control hub for Astral Party</strong>
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README_CN.md">简体中文</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT" />
  <img src="https://img.shields.io/badge/Hub-Tauri%202%20%7C%20Rust-orange.svg" alt="Tauri Rust" />
  <img src="https://img.shields.io/badge/Frontend-React%2019%20%7C%20TypeScript-blue.svg" alt="React TS" />
  <img src="https://img.shields.io/badge/Runtime-C%23%20%7C%20.NET-purple.svg" alt="C# .NET" />
  <img src="https://img.shields.io/badge/Platform-Windows-lightgrey.svg" alt="Windows" />
</p>

---

## Windows native migration candidate (on hold)

2026-10-22: Native migration is on hold at the user's request. The existing React / Tauri Hub and R7 game runtime remain the active baseline. Native sources and the local ZIP are preserved without replacing the installed app. See the [R7 verification checklist](R7_VERIFICATION_20261022.md) for background checks and pending live acceptance.

The locally built `0.2.0-native1` candidate uses WPF / .NET 10 for its interface and a Rust core built without Tauri or WebView. It includes 18 native pages, map guides, character progression details, and spectator following. The previous UI and published packages remain available; this candidate has not been uploaded or substituted for the group build.

Offline checks are complete. Live online validation is pending, and Spine/WebM playback plus the full LocalManaged management UI remain to be migrated. See [native build and validation notes](NATIVE_WINDOWS.md).

## Group distribution build

The current [group distribution build](https://github.com/Breezecaresses/Xingshu-hub/releases/tag/v0.1.0-test.20261022.2) is a prerelease, not the first stable version. Assets are grouped by desktop and Android. The desktop ZIP includes both Steam CN and TapTap Windows; extract it and run `星枢安装助手.exe`. Supports Steam CN and TapTap 3.3.0 / resource 026. The hand dock defaults to 60% overall size, adjustable from 40% to 120%. See [build notes](GROUP_DISTRIBUTION_20261022_2.md) for known limitations. The local-only native speed prototype is excluded.

The Android phone helper now pairs and connects to local wireless debugging inside its own APK. Android 11+ users can install, update, and restore using only their phone, without a computer or Shizuku notifications. Existing Shizuku installations remain optional. See the [phone installation guide](ANDROID_PHONE_INSTALL.md). Windows runtime r6 is retained and split into channel-specific assets; desktop ADB and manual Android files are also available.

---

## 📖 Overview

**XingShu (星枢)** is a local multi-process workstation and game enhancement runtime suite developed for *Astral Party* (吉星派对).

The project consists of two core components:
1. **AstralParty-Hub**: A modern desktop management hub built on Tauri 2 + Rust + React 19. It provides isolated account management, runtime lifecycle supervision, local loopback communication relay, and automated room following for spectating.
2. **XingShu Runtime**: A C# Unity enhancement module injected into the game client. It provides floating card status overlays, HUD widgets, match statistics, adaptive lifecycle reconciliation, and authoritative hand truth mirroring.

---

## Current Runtime Status

The GitHub repository stores source and version history. The Hub, main game, and authenticated spectator client currently run on the local computer. A remote spectator account pool has not been deployed.

- Resource 026 has been tested with a Steam CN main client and a TapTap spectator client: plugin startup, identity recognition, and four-player PVE hand mirroring work in the tested setup.
- The main client has also been tested across two PVE rooms without restarting the game or Hub; the avatar hand widgets displayed the new match's cards.
- Spectator commands use the bound `USER_SPECTATOR` platform and player identity. Bilibili, WeGame, and Steam Global still need client compatibility and end-to-end testing.
- Consecutive spectator matches, automatic departure after natural match completion, and card/coin/healing auto-thanks still need live verification. The tenfold thinking timer changes local timers; server-enforced deadlines still apply.
- Full hands currently come from authenticated official spectator messages. Login-free full-hand access remains unimplemented.

See [the local watcher notes](AstralParty-Hub/docs/local-managed-watcher.md) for verification details and remaining work.

## Hub and Runtime Update

- The character library includes 37 official configurations, including two unfinished preview entries. Missing names, skills, and artwork remain explicitly pending.
- Character, skin, bond, and growth details; full portrait card layouts and native animated alternate artwork; token and map/field-effect libraries.
- Match history and settlement details persist per account. PVE uses victory/defeat; PVP uses placement. Support transfers receive more rating weight; remaining gold stays visible and is excluded from ratings.
- Runtime, packages, plugins, and mods use five channels and explicit installation directories, including multiple locations per channel. Externally installed files show detection status only.
- Runtime management separates file verification, the actual game process, and the version reported by a confirmed connection. This bundle supplies verified runtime payloads for Steam CN and TapTap Windows; other channels still need runtime testing.
- The Hub sidebar, settings, and plugin F1 center link to the project source. The current desktop runtime version is `1.0.0-game-ui-r1`.
- The in-game center now uses translucent surfaces without background stripes, the game's font, and official star-shaped sliders. Colored text previews are fixed. Target selection keeps native panning and the user's zoom; labels and leader lines shrink when zooming out. See the [UI and camera validation record](GAME_UI_R1.md) for tested behavior and pending checks.

See [extension channel documentation](AstralParty-Hub/docs/specs/extension_channels.md) for manifests, destination mappings, and management boundaries. Game artwork, configuration, and resources remain the property of their respective owners and are not relicensed under the code's MIT license.

## Android Runtime Preview

`1.0.0-android-preview5` has been tested with the TapTap MuMu Android 12 game client (3.3.0, resource 026), including runtime loading, touch entry, and phone-helper installation/restore. The mobile button defaults to 64 UI units with a minimum of 48 and independent adjustment. Automatic edge hiding now persists correctly and leaves a tappable strip. Menu and text scaling remain independent; desktop defaults are unchanged.

Xiaomi 14 Pro (Android 16 / HyperOS 3) passed in-APK pairing, local connection, directory checks, official restore, and plugin installation, with device hashes independently verified. System wireless-debugging authorization is still required; Shizuku is optional. Xiaomi 15, other devices, and extended gesture/background scenarios have not been individually verified. The desktop bridge is paused on Android, and future server integration remains unimplemented. This is not an Android Hub APK. See [Android compatibility and device deployment](ANDROID_COMPATIBILITY.md) for the tested scope and build/restore commands.

## 🏗️ Architecture Overview

```mermaid
flowchart TD
    subgraph Hub["AstralParty-Hub (Tauri 2 / Rust + React 19)"]
        UI["Frontend Console (React + TS + Vite)"]
        FSM["Auto-Follow State Machine (11 States)"]
        TCPServer["Bridge TCP Server (127.0.0.1:25890)"]
        AccountMgr["Multi-Platform Account & Session Dispatcher"]
        UI <--> TCPServer
        FSM <--> TCPServer
        AccountMgr <--> FSM
    end

    subgraph RuntimeSpectator["XingShu Runtime (Bound Spectator Client)"]
        SpecHook["WatchLogic Interception Hooks"]
        SpecStore["SpectatorStateStore (Authoritative Hand Snapshot)"]
        SpecBridge["Bridge Client (Loopback TCP)"]
        SpecHook --> SpecStore
        SpecStore --> SpecBridge
    end

    subgraph RuntimeMain["XingShu Runtime (Bound Main Game Client)"]
        MainBridge["Bridge Client (Loopback TCP)"]
        TruthSource["HandTruthSource Authoritative Arbiter"]
        Reconcile["Overlay Lifecycle Adaptive Reconciliation"]
        FairyGUI["FairyGUI Floating Hand Overlay / Match HUD"]
        MainBridge --> TruthSource
        TruthSource --> Reconcile
        Reconcile --> FairyGUI
    end

    TCPServer <== "Loopback TCP (JSON Frames)" ==> SpecBridge
    TCPServer <== "HAND_TRUTH_MIRROR" ==> MainBridge
```

---

## ✨ Key Features

- **Authoritative Hand Truth System (`HandTruthSource`)**:
  - The full-hand mirror passes through the Hub over `127.0.0.1` loopback communication. Official game authentication and spectating use the game's own server connections.
  - **No Fake/Placeholder Cards**: Rendering forged placeholder cards (`CardId = -1`) is strictly forbidden; hand card data is strictly validated against the match hero roster.
  - Once the spectator client acquires complete authoritative data, it is mirrored and pushed to the main game client in real-time via the local Hub.
- **Deterministic Overlay Lifecycle Reconciliation**:
  - Solves issues where FairyGUI widgets detach or fail to auto-initialize due to node unmounting or scene transitions.
  - Driven by 10 distinct lifecycle events with an adaptive two-tier polling and retry mechanism, ensuring hand overlays remain precisely aligned with player avatars at all times.
- **Secure & Isolated Auto-Follow Protocol**:
  - Managed by a strict 11-state finite state machine that automatically detects matches in the main game and guides the spectator client into the room.
  - **Bound Account Routing**: Auto-follow instructions target the configured spectator role, platform, and current player identity, with connection and match checks separating it from the main account.
- **Multi-Platform Account Binding & Scheduling**:
  - Supports concurrent management and routed launching of multiple accounts across Steam, TapTap, and other platforms.
  - Automatically cleans up overlay state, vacates spectator rooms, and resets match fingerprints across game sessions.

---

## 📁 Directory Structure

```text
XingShu/
├── AstralParty-Hub/            # Desktop management console
│   ├── src/                    # Frontend source (React 19, TypeScript, TailwindCSS)
│   │   ├── pages/              # UI pages (Spectator, Account Center, Runtime, Diagnostics, etc.)
│   │   ├── services/           # Core services (Auto-follow, Truth Arbiter, Account Management)
│   │   └── shared/             # Shared card registry and data models
│   ├── src-tauri/              # Tauri Rust backend
│   │   ├── src/xing_shu/       # Local TCP bridge server, protocol codec, spectator stream
│   │   └── Cargo.toml          # Rust dependency configuration
│   └── package.json            # Frontend dependency configuration
├── XingShu-Dev/                    # In-game C# runtime module
│   ├── src/XingShu.Runtime/          # XingShu core logic (HandTruthSource, UI widgets, protocol client)
│   ├── tools/                  # MSBuild scripts, lifecycle tests, and bootstrapping tools
│   └── assets/                 # In-game UI assets and icons (vanilla-ui-r3)
├── tools/                      # Automation tooling (registry generators, patch scripts, source sync)
├── AUTO_FOLLOW_PROTOCOL.md     # Auto-follow local control protocol specification
├── AUTO_FOLLOW_STATE_MACHINE.md# Auto-follow 11-state finite state machine specification
├── HAND_TRUTH_SOURCE_ARCHITECTURE.md # Hand truth source & single-source-of-truth architecture
├── OVERLAY_LIFECYCLE_RECONCILIATION.md # Deterministic overlay lifecycle reconciliation specification
├── runtime_card_registry.generated.json # Authoritative card definition registry
└── LICENSE                     # Open source license (MIT)
```

---

## 🛠️ Build & Setup Guide

> [!NOTE]
> In compliance with open-source licensing and distribution ethics, this repository **does not contain** any proprietary original game binaries (DLLs), sensitive account credentials, or proprietary assets.

### 1. AstralParty-Hub (Desktop Console)

#### Prerequisites
- Node.js (v18+ LTS recommended)
- Rust & Cargo (latest stable toolchain)
- Visual Studio C++ Build Tools (Windows C++ Build Tools)
- Microsoft Edge WebView2

#### Build Steps
```bash
# Navigate to the Hub directory
cd AstralParty-Hub

# Install dependencies
npm ci

# Build Tauri desktop application (unpackaged binary mode)
npm run tauri -- build --no-bundle
```

---

### 2. XingShu Runtime (C# Game Runtime)

#### Prerequisites
- Visual Studio MSBuild with C# 11 support; the project targets .NET Framework 4.0 using the game's reference assemblies
- Python 3.x
- Mono.Cecil and Mono.Cecil.Rocks (placed in `XingShu-Dev/tools/lib`)

#### Dependency Setup
Extract the matching version of `AstralParty.Runtime.dll` from your legally owned game client and place it in the default path:
`XingShu-Dev/references/updated_20261019_verified/` (or specify the `AstralPartyRuntimePath` MSBuild property in your build configuration).
Place other dependency assemblies into `XingShu-Dev/references/{unity,framework,thirdparty}` as needed.

#### Build Steps
```powershell
# Execute build script
.\XingShu-Dev\tools\build.ps1 -Configuration Release
```

---

When refreshing this checkout from the development workspace, run `python tools/export_source_repo.py <source-checkout-path>` there and review the diff. Existing README files and `.gitignore` are maintained in this checkout and preserved by the export tool.

## 📚 Detailed Documentation

- [AUTO_FOLLOW_PROTOCOL.md](AUTO_FOLLOW_PROTOCOL.md) — Auto-Follow Local TCP Communication Protocol Specification
- [AUTO_FOLLOW_STATE_MACHINE.md](AUTO_FOLLOW_STATE_MACHINE.md) — Auto-Follow 11-State Finite State Machine Design & Transition Matrix
- [HAND_TRUTH_SOURCE_ARCHITECTURE.md](HAND_TRUTH_SOURCE_ARCHITECTURE.md) — Single Source of Truth Architecture & Counter-Forgery Design
- [OVERLAY_LIFECYCLE_RECONCILIATION.md](OVERLAY_LIFECYCLE_RECONCILIATION.md) — Floating Overlay Adaptive Lifecycle Reconciliation Specification
- [local-managed-watcher.md](AstralParty-Hub/docs/local-managed-watcher.md) — Locally Managed Watcher Work Unit Technical Guide

---

## ⚠️ Disclaimer & Ethical Boundaries

1. This project is intended solely for technical research, local automated testing, and educational purposes.
2. This project does not distribute or bundle any proprietary assemblies, decompiled artifacts, or commercial assets from the original game. Users must provide their own legitimately obtained game installations.
3. Please use companion utilities responsibly and adhere to the official Terms of Service and End User License Agreement of the game. The developers assume no liability for any account penalties, bans, or losses resulting from misuse.

---

## 📄 License

This project is open-source under the [MIT License](LICENSE).
