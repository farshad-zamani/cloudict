# Architecture

Cloudict is a Windows desktop app (C# / .NET 7, WPF + Windows Forms
interop) that performs speech-to-text by automating the **Google Translate** web page and
types the recognized text into whichever application currently has focus. It also supports
user-defined **voice commands**.

## High-level flow

```
        ┌────────────┐   global hotkey / button
        │  MainWindow │ ─────────────────────────┐
        └─────┬──────┘                            │
              │ drives (Selenium WebDriver)       │ types via InputSimulator
              ▼                                    ▼
    ┌───────────────────┐                 ┌────────────────────┐
    │  Google Translate  │  recognized    │  Active foreground   │
    │  page in Chrome    │ ───text──────▶ │  application         │
    └───────────────────┘                 └────────────────────┘
```

1. `App.OnStartup` applies the saved UI language, ensures a single instance, requests
   administrator rights, then opens `MainWindow`.
2. `MainWindow` launches Chrome via Selenium/WebDriverManager, opens Google Translate,
   and clicks the microphone button.
3. As Google transcribes speech, the recognized text is read from the page and sent,
   word by word, into the active application using `H.InputSimulator`.
4. Recognized words are matched against **voice commands**; a match runs an action
   (type punctuation, send a key, switch keyboard language, launch a program, …).

## Folder layout

| Path | Purpose |
|------|---------|
| `src/Cloudict/Views/` | WPF windows: `MainWindow`, `SettingsWindow`, `AddCommandWindow`, `KeySelectionWindow`, `DesktopStatusIndicator` |
| `src/Cloudict/Services/` | `VoiceCommandManager`, `VoiceCommandProcessor`, `SystemCommandExecutor`, `WordTracker`, `SettingsManager`, `GlobalShortcutManager`, `NotificationManager` |
| `src/Cloudict/Models/` | `AppSettings`, `VoiceCommand` |
| `src/Cloudict/Localization/` | `LocalizationManager` + `Strings/Strings.<lang>.xaml` dictionaries |
| `src/Cloudict/Assets/` | Application icon and the Inter & Vazirmatn fonts |

## Key components

- **LocalizationManager** — loads the selected language's `ResourceDictionary` (English
  first as a fallback, then the chosen language on top) and exposes `Loc.Get("Key")` for
  code-behind. Language is applied at startup; switching requires a restart. Flow
  direction (RTL/LTR) is published as the `AppFlowDirection` dynamic resource that every
  window binds to.
- **SettingsManager** — JSON persistence (`settings.json`) with a backup copy and
  validation. `AppSettings` holds all tunables (delays, Google Translate selectors,
  shortcuts, voice commands, and the UI language).
- **VoiceCommandProcessor / SystemCommandExecutor** — detect spoken command phrases and
  execute the corresponding system action.
- **GlobalShortcutManager** — registers system-wide hotkeys (default `Ctrl+Alt+A` to
  start/stop and `Ctrl+Alt+S` to stop).

## Notable dependencies
Selenium.WebDriver + WebDriverManager (browser automation), H.InputSimulator (keystroke
injection), NAudio, Newtonsoft.Json, Polly, AngleSharp, SharpZipLib.

## Caveats
The recognition engine depends on the **public Google Translate web UI**. If Google
changes that page, the selectors in *Settings → Google Translate Settings* may need to be
updated. This approach is inherently fragile; migrating to a dedicated speech-to-text
engine (e.g. Whisper or a cloud STT API) is on the roadmap.
