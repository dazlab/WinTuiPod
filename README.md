![Header](screenshots/github-header-banner-dev.png)

[![License](https://img.shields.io/github/license/dazlab/WinTuiPod)](LICENSE)
[![Issues](https://img.shields.io/github/issues/dazlab/WinTuiPod)](https://github.com/dazlab/WinTuiPod/issues)
![Downloads](https://img.shields.io/github/downloads/dazlab/WinTuiPod/total?cacheBust=2)
[![Build](https://github.com/dazlab/WinTuiPod/actions/workflows/dotnet.yml/badge.svg?branch=master&cacheBust=1)](https://github.com/dazlab/WinTuiPod/actions/workflows/dotnet.yml)
[![Dev](https://github.com/dazlab/WinTuiPod/actions/workflows/dotnet.yml/badge.svg?branch=development&cacheBust=1)](https://github.com/dazlab/WinTuiPod/actions/workflows/dotnet.yml)

## Current Development Branch Diffs with `master`
Implemented:
- [ ✅ ] Live Radio Broadcast

## Part of a TUI Suite

WinTuiPod is part of a small family of Windows-native TUI applications:

- **[WinTuiRss](https://github.com/dazlab/WinTuiRss)** — a terminal RSS reader for Windows  
- **[WinTuiNotes](https://github.com/dazlab/WinTuiNotes)** - a terminal notes management app
- **[WinTuiEditor](https://github.com/dazlab/WinTuiEditor)** — a simple, terminal-based text editor  
- **WinTuiPod** — podcasts, from the terminal

## Status

Active development is done in the development branch. Features, behaviour, and UI are still evolving and the current
development build may not even build at the time you clone into it, so if you're looking to try it out, use the
[Release Build](https://github.com/dazlab/WinTuiPod/releases) page to download a version release.

Contributions, feedback, and bug reports are welcome.

## Requirements

- For development: .NET 8 SDK
- For running: none if you use the self-contained release build (single EXE)

## Install (from source)

```powershell
git clone https://github.com/dazlab/WinTuiRss.git
cd WinTuiRss\WinTuiRss
dotnet restore
dotnet run
```

## License
MIT. 

See [License](LICENSE)

## Third-party notices
See [Third-party notices](THIRD-PARTY-NOTICES.md)x
