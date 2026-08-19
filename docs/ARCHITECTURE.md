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
2. `MainWindow` resolves Chrome + ChromeDriver through `BrowserProvisioner`, launches Chrome
   via Selenium, opens Google Translate, and clicks the microphone button.
3. As Google transcribes speech, the recognized text is read from the page and sent,
   word by word, into the active application using `H.InputSimulator`.
4. Recognized words are matched against **voice commands**; a match runs an action
   (type punctuation, send a key, switch keyboard language, launch a program, …).

## Project layout

Cloudict is being made cross-platform. The code is split so that the parts which can run anywhere
are physically separated from the parts that cannot, rather than relying on discipline:

| Project | Target | Contains |
|---------|--------|----------|
| `src/Cloudict.Core/` | `net10.0` | Application logic with **no UI framework and no OS calls**: `AppSettings`, `VoiceCommand`, `WordTracker`, `VoiceCommandProcessor`, `VoiceCommandManager`, `SettingsManager`, `KeyCommandParser`, `BrowserProvisioner`, and the `Abstractions/` interfaces |
| `src/Cloudict.Platform/` | `net10.0` | **Every** operating-system call, behind those interfaces — `Windows/`, `Unix/`, and `Unsupported/` stand-ins, selected at runtime by `PlatformServices.Create()` |
| `src/Cloudict/` | `net10.0-windows` | The Windows/WPF shell: `Views/`, `Themes/`, `Localization/`, `Assets/`, `Drivers/`, and `AppServices` (the composition root) |
| `src/Cloudict.Core.Tests/` | `net10.0` | xunit tests for Core |

Core compiling on its own — with no `UseWPF`, no runtime identifier and no reference to
`System.Windows.*` — is what proves the separation holds. A Windows-ism cannot leak back in without
failing the build.

### The platform abstraction

`Cloudict.Core/Abstractions` declares what the app needs from an operating system:

| Interface | Purpose |
|-----------|---------|
| `ITextInjector` | Types text and presses keys in the focused application — the core capability |
| `IGlobalHotkeys` | System-wide start/stop shortcuts |
| `IKeyboardLayout` | Switches the system keyboard layout for the language voice commands |
| `IMicrophoneMonitor` | Whether the microphone is live, for the desktop status light |
| `IAppPaths` | Where settings, data and logs may be written |
| `IPlatformInfo` / `IBrowserLocator` | Finding Chrome and picking the right ChromeDriver build |

Each reports its own availability (`IsAvailable`, `IsSupported`) with a localization key explaining
any limitation, because a missing capability is a normal condition on Linux and macOS — Wayland has
no injection API, macOS withholds one until the user grants Accessibility — and the app is expected
to keep running and explain itself rather than appear broken.

`PlatformServices.Create()` is the only place in the codebase that asks which OS it is running on.

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
- **BrowserProvisioner** — finds Chrome and a compatible ChromeDriver *without needing the
  network*. See below; this is the component that makes startup reliable.

## Browser provisioning

ChromeDriver must match the installed Chrome's major version, and Chrome updates itself, so
"which driver do we run?" is a moving target. Cloudict used to delegate this to
`WebDriverManager`, which downloaded the driver from Google on every startup. That made the app
unusable wherever `storage.googleapis.com` is blocked — it answers **403 Forbidden**, or the TLS
handshake is intercepted — and it broke working installs the moment Chrome auto-updated.

`BrowserProvisioner.Resolve()` now runs disk-first:

```
detect Chrome (App Paths registry + standard install folders)
        │
        ▼
collect every chromedriver.exe on the machine
  Drivers\ (bundled)  ·  %LOCALAPPDATA%\Cloudict\Drivers (our cache)
  Chrome\ (legacy cache)  ·  %LOCALAPPDATA%\ChromeDriver
  ~\.cache\selenium\chromedriver  ·  PATH
        │
        ├─ a driver with Chrome's major version?  ──▶ newest one wins. Done, offline.
        │
        ├─ otherwise download it once, into our per-user cache.
        │     mirrors first (cdn/registry.npmmirror.com), Google's host last.
        │
        └─ otherwise use the closest driver we have, with
           ChromeDriverService.DisableBuildCheck = true.
```

Two properties matter and are deliberate:

- **A driver already on the machine is never overwritten or downgraded.** The bundled driver is
  a floor, not a ceiling — if the user's Chrome is newer and they already have a matching
  driver, theirs is used.
- **Downloads never write into the install folder**, only `%LOCALAPPDATA%\Cloudict\Drivers`, so
  provisioning needs no elevation and can't corrupt the shipped copy.

The version bundled in `src/Cloudict/Drivers/` is committed so a fresh clone can build an
offline-capable installer; refresh it with `scripts\fetch-chromedriver.ps1`.

## Notable dependencies
Selenium.WebDriver (browser automation), H.InputSimulator (keystroke injection), NAudio,
Newtonsoft.Json, Polly, AngleSharp, SharpZipLib.

## Caveats
The recognition engine depends on the **public Google Translate web UI**. If Google
changes that page, the selectors in *Settings → Google Translate Settings* may need to be
updated. This approach is inherently fragile; migrating to a dedicated speech-to-text
engine (e.g. Whisper or a cloud STT API) is on the roadmap.
