# Cloudict

> Free, open-source **voice typing for Windows**, powered by **Google's speech recognition**
> (through the Google Translate website) — so you can type by voice into *any* app, **for
> free**, in **many of the world's languages**, with a built-in voice-command system and a
> bilingual (English / Persian) interface.

<p align="center">
  <em>Built by <a href="https://cloudtart.com">Farshad Zamani · cloudtart.com</a></em>
</p>

English | [فارسی](README.fa.md)

---

## What it does

Cloudict turns your speech into text and types it directly into whatever app is in focus —
your editor, browser, chat, Word, anywhere. Think of it as **Google's voice typing (like
Gboard on Android), but for any Windows app**: it uses **Google's speech recognition — among
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
- 🌍 **Dictate in many languages** — pick your speech/typing language from 20+ options
  (English, Persian, Arabic, French, German, Spanish, Russian, Hindi, Chinese, and more).
- 🌐 **Bilingual UI** — English by default, Persian selectable (and easy to add more
  languages). Full RTL/LTR support.
- ⌨️ **Global hotkeys** — `Ctrl+Alt+A` to start/stop, `Ctrl+Alt+S` to stop (configurable).
- 🗣️ **Voice commands** — punctuation, special keys, keyboard-language switching, launching
  programs.
- 🛠️ **Tunable** — adjust transfer delays and Google Translate selectors from Settings.
- 🪟 **System-tray friendly** — optionally minimize to tray to keep hotkeys active.

## Screenshots

_Add screenshots to `docs/screenshots/` and reference them here._

## Requirements

- Windows 10 / 11 (x64)
- **Google Chrome installed** — Cloudict drives a Chrome window as its helper browser. Chrome
  specifically: Chromium builds are compiled without Google's API keys, so the speech
  recognition Google Translate relies on silently does nothing there.
- Administrator rights (needed for global hotkeys and sending keystrokes to other apps)
- For building from source: [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)

### About the bundled browser driver

Cloudict talks to Chrome through **ChromeDriver**, which must match your Chrome's major
version. The installer **ships a ChromeDriver**, so the app works the moment it is installed —
no download, no waiting, and nothing to configure, even with no internet connection.

Chrome updates itself, so sooner or later it moves past the bundled driver. Cloudict handles
that on its own, in this order:

1. **Any newer, matching driver already on your machine wins.** Cloudict scans its own
   folder, its download cache, `%LOCALAPPDATA%\ChromeDriver`, Selenium's cache and your `PATH`,
   and always picks the newest driver matching your Chrome. A driver you already had is never
   overwritten, replaced, or downgraded by the one in our installer.
2. **If your Chrome is newer than every driver on the machine**, Cloudict fetches the matching
   one *once*, into `%LOCALAPPDATA%\Cloudict\Drivers` (never the install folder). Mirrors are
   tried before Google's own host, which is unreachable from some regions.
3. **If that download can't happen either** (offline or blocked), Cloudict falls back to the
   closest driver it has rather than refusing to start.

So the only thing you ever need is Chrome itself.

## Install

Download the latest **`Cloudict-x.y.z-Setup.exe`** from the [Releases](../../releases) page and
run it — the app is self-contained, so no .NET installation is required.

To build the installer yourself, run `scripts\build-installer.bat` (publishes the app, then
compiles [`installer/Cloudict.iss`](installer/Cloudict.iss) with
[Inno Setup 6](https://jrsoftware.org/isdl.php)). The result lands in `installer/Output/`.

## Build & run from source

```bash
git clone https://github.com/farshad-zamani/cloudict.git
cd cloudict

# Run
dotnet run --project src/Cloudict/Cloudict.csproj

# Or open src/Cloudict.sln in Visual Studio 2022
```

### Create a distributable build
```bash
scripts\publish.bat
```
This produces a self-contained folder under
`src/Cloudict/bin/Release/.../publish/`. Run `Cloudict.exe`
from that folder (no .NET install required). Zip the whole folder to distribute it.

> The ChromeDriver that ships with the app lives in `src/Cloudict/Drivers/` and **is**
> committed, so a fresh clone builds an installer that works offline. To bundle the driver for
> a newer Chrome, run `powershell -ExecutionPolicy Bypass -File scripts\fetch-chromedriver.ps1`
> (it replaces the driver in place, taking the current Chrome-for-Testing stable by default).

## Usage

1. Launch the app and accept the administrator prompt.
2. Click the browser button to open Google Translate, then start listening
   (button or `Ctrl+Alt+A`).
3. Speak — your words are typed into the app that has focus.
4. Manage delays, shortcuts, and voice commands in **Settings**.

### Switching language
Open **Settings → General Settings → Language**, choose English or فارسی, save, and restart
the app. English is the default and the fallback for any untranslated text.

## Tips for best results

- **Don't minimize the helper browser window.** When you open the helper browser (the
  Chromium / Chrome window that loads Google Translate), keep it open. You may place other
  windows **on top of** it — but if you **minimize** it, recognition stops and typing breaks.
- **Tune the delays to your setup.** Because Cloudict works *through* the live Google
  Translate page, a few timing delays keep the two in sync (Google Translate keeps revising
  each word as you keep speaking). The defaults work for most people, but you may need to
  fine-tune them in **Settings → Text Transfer Delays** — hover any field to see exactly what
  it does and why — until typing stays perfectly in sync with your voice. Once tuned, just
  speak and it types.

## Project structure

```
src/Cloudict/
├─ Views/         WPF windows (XAML + code-behind)
├─ Services/      speech transfer, voice commands, settings, shortcuts, notifications
├─ Models/        AppSettings, VoiceCommand
├─ Localization/  LocalizationManager + Strings/Strings.<lang>.xaml
└─ Assets/        icon + Inter & Vazirmatn fonts
```
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for details.

## Project history

Cloudict is a mature application, developed and refined over a long period before being
published as open source. See the [CHANGELOG](CHANGELOG.md) for the release history.

## Roadmap

- Replace Google-Translate automation with a dedicated STT engine (Whisper / cloud STT)
  for accuracy, offline use, and **any-language dictation worldwide**.
- More UI languages (contributions welcome — see [CONTRIBUTING.md](CONTRIBUTING.md)).

## Contributing

Issues and pull requests are welcome — including new translations. See
[CONTRIBUTING.md](CONTRIBUTING.md).

## Credits

Created by **Farshad Zamani** — [cloudtart.com](https://cloudtart.com).
Available for website design and software projects worldwide.

## License

Released under the [MIT License](LICENSE) — free to use, modify, and distribute.
