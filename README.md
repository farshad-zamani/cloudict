# Cloudict

> Free, open-source **voice typing for Windows, Linux and macOS**, powered by **Google's speech
> recognition** (through the Google Translate website) — so you can type by voice into *any* app,
> **for free**, in **many of the world's languages**, with a built-in voice-command system and a
> bilingual (English / Persian) interface.

<p align="center">
  <em>Built by <a href="https://cloudtart.com">Farshad Zamani · cloudtart.com</a></em>
</p>

English | [فارسی](README.fa.md)

---

## What it does

Cloudict turns your speech into text and types it directly into whatever app is in focus —
your editor, browser, chat, Word, anywhere. Think of it as **Google's voice typing (like
Gboard on Android), but for any desktop app**: it uses **Google's speech recognition — among
the most accurate available — completely free**, by automating the **Google Translate** web
page through a Chrome session. No paid API, no account, and no local model.

It also includes a **voice-command** system: say a keyword to insert punctuation, press a
key (Enter, Tab, …), switch the keyboard language, or run a program.

> ℹ️ **How recognition works (and its trade-off):** recognition relies on the public
> Google Translate web UI driven via Selenium. It's free and needs no API key, but it can
> break if Google changes that page. The selectors are editable in *Settings → Google
> Translate Settings*. Moving to a dedicated engine (e.g. Whisper) is on the roadmap.

## Features

- 🎙️ **Dictate anywhere** — recognized text is typed into the active window.
- 🖥️ **Windows, Linux and macOS** — one application, one interface, three systems.
- 🌍 **Dictate in many languages** — pick your speech/typing language from 20+ options
  (English, Persian, Arabic, French, German, Spanish, Russian, Hindi, Chinese, and more).
- 🌐 **Bilingual UI** — English by default, Persian selectable (and easy to add more
  languages). Full RTL/LTR support.
- ⌨️ **Global hotkeys** — `Ctrl+Alt+A` to start/stop, `Ctrl+Alt+S` to stop (configurable).
- 🗣️ **Voice commands** — punctuation, special keys, keyboard-language switching, launching
  programs.
- 🛠️ **Tunable** — adjust transfer delays and Google Translate selectors from Settings.
- 🔌 **Works offline on first run** — the matching browser driver ships inside every package.

## Requirements

- **Google Chrome.** Cloudict drives a Chrome window as its helper browser. Chrome specifically:
  Chromium builds are compiled without Google's API keys, so the speech recognition Google
  Translate relies on silently does nothing there.
- Windows 10/11 (x64), a current Linux distribution (x64), or macOS 11+ (Apple Silicon or Intel).

Everything else is bundled. Cloudict is self-contained — there is no .NET to install.

## Install

Download the package for your system from the [Releases](../../releases) page.

### Windows

Run **`Cloudict-x.y.z-Setup.exe`**.

Cloudict no longer requires administrator rights to run. The one thing it cannot do without them
is type into a window that is *itself* running as administrator — Windows refuses synthetic input
from a lower integrity level. If you need that, start Cloudict as administrator.

### Linux

```bash
sudo apt install ./cloudict_x.y.z_amd64.deb      # Debian, Ubuntu, Mint
sudo dnf install ./cloudict-x.y.z-1.x86_64.rpm   # Fedora, RHEL, openSUSE
chmod +x Cloudict-x.y.z-x86_64.AppImage && ./Cloudict-x.y.z-x86_64.AppImage   # anything else
```

**X11 sessions work with no further setup.**

**Wayland needs one extra step.** Wayland deliberately gives no application a way to type into
another, so Cloudict uses `ydotool`, which goes through the kernel's virtual-input device:

```bash
sudo apt install ydotool
systemctl --user enable --now ydotool
```

Wayland also prevents any application from claiming a global shortcut. Bind one in your desktop's
own keyboard settings instead, pointing it at:

```
cloudict --toggle
```

Not sure what your system supports? Run `cloudict --diagnose`; it prints exactly what was detected.

### macOS

Open the `.dmg` and drag Cloudict to Applications. Pick the **arm64** build for Apple Silicon and
**x64** for Intel.

The build is **not signed by Apple** (that needs a paid Developer Program membership), so
Gatekeeper blocks it the first time. Either right-click the app and choose **Open**, or:

```bash
xattr -dr com.apple.quarantine /Applications/Cloudict.app
```

macOS then asks for **Accessibility** permission the first time Cloudict types. Grant it in
*System Settings → Privacy & Security → Accessibility*; without it macOS silently discards every
keystroke Cloudict sends.

## Usage

1. Launch Cloudict.
2. Click **Open helper browser** to open Google Translate, then press the green button (or
   `Ctrl+Alt+A`) to start listening.
3. Speak — your words are typed into the app that has focus.
4. Manage delays, shortcuts and voice commands in **Settings**.

Prefer to collect text inside Cloudict instead of typing into another app? Leave **Transfer to
cursor position** off and the words gather in the *Final text* box.

### Command line

| Command | Effect |
|---------|--------|
| `cloudict --toggle` | Start or stop dictation in the running instance |
| `cloudict --start` / `--stop` | Start or stop explicitly |
| `cloudict --diagnose` | Print what this system supports, and where Chrome and the driver were found |
| `cloudict --version` | Print the version |

### Switching language

Open **Settings → General Settings → Interface language**, choose English or فارسی, save, and
restart. English is the default and the fallback for any untranslated text.

## Tips for best results

- **Don't minimize the helper browser window.** Keep it open; you may place other windows **on top
  of** it, but minimizing it stops recognition.
- **Tune the delays to your setup.** Cloudict works *through* the live Google Translate page,
  which keeps revising each word as you speak, so a few timing delays keep the two in sync. The
  defaults suit most people; adjust them in **Settings → Text Transfer Delays** until typing stays
  in step with your voice.

## About the bundled browser driver

Cloudict talks to Chrome through **ChromeDriver**, which must match your Chrome's major version.
Every package **ships one**, so the app works the moment it is installed — no download, no waiting,
nothing to configure, even with no internet connection.

Chrome updates itself, so sooner or later it moves past the bundled driver. Cloudict handles that:

1. **Any newer, matching driver already on your machine wins.** Cloudict scans its own folder, its
   download cache, the system driver locations, Selenium's cache and your `PATH`, and always picks
   the newest driver matching your Chrome. A driver you already had is never overwritten or
   downgraded by the bundled one.
2. **If your Chrome is newer than every driver present**, Cloudict fetches the matching one *once*,
   into its per-user data directory — never the install folder. Mirrors are tried before Google's
   own host, which is unreachable from some regions.
3. **If that download can't happen either** (offline or blocked), Cloudict falls back to the
   closest driver it has rather than refusing to start.

So the only thing you ever need is Chrome itself.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/farshad-zamani/cloudict.git
cd cloudict

dotnet run --project src/Cloudict.App          # run
dotnet test src/Cloudict.Core.Tests            # tests
```

### Packages

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

Each package needs tooling that only exists on its own operating system — Inno Setup for the
Windows installer, `dpkg`/`rpmbuild` for the Linux packages, and macOS-only tooling for the `.app`
bundle — so one machine rarely builds all of them. The
[release workflow](.github/workflows/release.yml) builds the full set using one runner per platform.

To bundle drivers for a newer Chrome:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\fetch-chromedriver.ps1
```

## Project structure

```
src/
├─ Cloudict.Core/       application logic — no UI framework, no OS calls
├─ Cloudict.Platform/   every OS call, behind interfaces, chosen at runtime
├─ Cloudict.App/        the Avalonia interface
└─ Cloudict.Core.Tests/ tests for Core
packaging/              windows · linux · macos
```

Core compiles on its own, with no UI framework and no runtime identifier — which is what keeps a
platform-specific assumption from leaking back in unnoticed. See
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Roadmap

- Replace Google-Translate automation with a dedicated STT engine (Whisper / cloud STT)
  for accuracy, offline use, and **any-language dictation worldwide**.
- More UI languages (contributions welcome — see [CONTRIBUTING.md](CONTRIBUTING.md)).
- Signed and notarised macOS builds.

## Contributing

Issues and pull requests are welcome — including new translations. See
[CONTRIBUTING.md](CONTRIBUTING.md).

## Credits

Created by **Farshad Zamani** — [cloudtart.com](https://cloudtart.com).
Available for website design and software projects worldwide.

## License

Released under the [MIT License](LICENSE) — free to use, modify, and distribute.
