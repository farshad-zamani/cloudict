# Cloudict

> Free, open-source **speech-to-text dictation for Windows** that types what you say
> *anywhere*, in **many languages** — with a built-in voice-command system and a bilingual
> (English / Persian) interface.

<p align="center">
  <em>Built by <a href="https://cloudtart.com">Farshad Zamani · cloudtart.com</a></em>
</p>

English | [فارسی](README.fa.md)

---

## What it does

Cloudict turns your speech into text and types it directly into whatever
app is in focus — your editor, browser, chat, Word, anywhere. It recognizes speech by
automating the **Google Translate** web page through a headless-style Chrome session, so it
works without any paid API or local model.

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
- Google Chrome installed
- Administrator rights (needed for global hotkeys and sending keystrokes to other apps)
- For building from source: [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)

## Install

Download the latest **`Cloudict-x.y.z-Setup.exe`** from the [Releases](../../releases) page and
run it — the app is self-contained, so no .NET installation is required.

To build the installer yourself, run `scripts\build-installer.bat` (publishes the app, then
compiles [`installer/Cloudict.iss`](installer/Cloudict.iss) with
[Inno Setup 6](https://jrsoftware.org/isdl.php)). The result lands in `installer/Output/`.

## Build & run from source

```bash
git clone <your-fork-url>
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

> The `Chrome/` driver folder is **not** committed — `WebDriverManager` downloads the
> matching ChromeDriver automatically at runtime.

## Usage

1. Launch the app and accept the administrator prompt.
2. Click the browser button to open Google Translate, then start listening
   (button or `Ctrl+Alt+A`).
3. Speak — your words are typed into the app that has focus.
4. Manage delays, shortcuts, and voice commands in **Settings**.

### Switching language
Open **Settings → General Settings → Language**, choose English or فارسی, save, and restart
the app. English is the default and the fallback for any untranslated text.

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
