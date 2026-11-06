# Architecture

Cloudict is a desktop voice-typing application for Windows, Linux and macOS. It has no speech model
of its own: it drives the public Google Translate voice input in a helper Chrome window, reads what
that page recognizes, and types the words into whatever application has focus.

## Project layout

The code is split so that the parts which can run anywhere are *physically* separated from the
parts that cannot, rather than relying on discipline:

| Project | Target | Contains |
|---------|--------|----------|
| `src/Cloudict.Core/` | `net10.0` | Application logic with **no UI framework and no OS calls**: settings, voice commands, the speech engine, the dictation session, localization, and the `Abstractions/` interfaces |
| `src/Cloudict.Platform/` | `net10.0` | **Every** operating-system call, behind those interfaces — `Windows/`, `Linux/`, `MacOS/`, `Unix/`, and `Unsupported/` stand-ins, selected at runtime |
| `src/Cloudict.App/` | `net10.0` | The Avalonia interface: views, themes, icons, the composition root |
| `src/Cloudict.Core.Tests/` | `net10.0` | Tests for Core |
| `packaging/` | — | `windows/` (Inno Setup), `linux/` (deb, rpm, AppImage), `macos/` (app bundle, dmg) |

Core compiling on its own — no UI framework, no runtime identifier, no reference to
`System.Windows.*` — is what proves the separation holds. A platform-specific assumption cannot leak
back in without failing the build.

## The platform abstraction

`Cloudict.Core/Abstractions` declares what the application needs from an operating system:

| Interface | Windows | Linux | macOS |
|-----------|---------|-------|-------|
| `ITextInjector` | `SendInput` with `KEYEVENTF_UNICODE` | XTEST, or `ydotool` on Wayland | `CGEvent` with a Unicode payload |
| `IGlobalHotkeys` | `RegisterHotKey` on a dedicated thread | `XGrabKey` on the root window | Carbon `RegisterEventHotKey` |
| `IKeyboardLayout` | `LoadKeyboardLayout` | not implemented | not implemented |
| `IMicrophoneMonitor` | WASAPI via NAudio | not implemented | not implemented |
| `IAppPaths` | `%APPDATA%` / `%LOCALAPPDATA%` | XDG directories | `~/Library/Application Support` |
| `IPlatformInfo`, `IBrowserLocator` | registry + Program Files | standard paths, flatpak, snap | `/Applications`, `Info.plist` |

Each reports its own availability with a localization key explaining any limitation, because a
missing capability is a *normal* condition here — Wayland has no injection API, macOS withholds one
until Accessibility is granted — and the application is expected to keep running and explain itself
rather than appear broken. `PlatformServices.Create()` is the only place in the codebase that asks
which operating system it is running on.

`cloudict --diagnose` prints all of this, which is usually the fastest way to answer "why does
dictation type nothing on my machine".

## How dictation works

```
Google Translate page (helper Chrome window)
        │  GoogleTranslateEngine: open, toggle the voice button, read the source box
        ▼
DictationSession: pace words, run voice commands, decide where they go
        │
        ├── live      → ITextInjector.TypeText  → the focused application
        └── buffered  → IDictationOutput        → the "Final text" box, at the caret
```

**The timing is the difficult part**, and every delay exists for a reason found the hard way. Google
Translate keeps *revising* words it has already shown while the user carries on speaking, so text
cannot simply be forwarded as it appears. `DictationSession` waits before starting
(`TransferStartDelayMs`), paces itself word by word (`WordByWordDelayMs`), and after a silence
(`InactivityDelayMs`) flushes what is pending and restarts the microphone so the page's buffer never
grows long enough to be rewritten wholesale. These normal pause/continuous-run resets, including
their start retries, are independent of `KeepMicrophoneActive`. That option (default true) controls
only recovery when the page stops its own microphone. With it disabled, three confirmed page-off
samples flush the final tail once and request Stop without clearing or restarting the page.
Only a reset explicitly requested as keep-alive recovery may be cancelled by changing that option;
normal dictation resets always finish, unless the user presses Stop or the machine suspends.

Two guards in there are worth knowing about, because both fixed real bugs:

- A reset only fires when a word was actually transferred since the last one. Without that, an idle
  pause triggered a reset that re-sent text already typed.
- The leading-space decision is made at *send* time, reading the up-to-date destination. Deciding it
  when the word was queued raced with the previous word's delayed send, and two words dispatched
  close together arrived with no space between them.

**Voice commands are matched on the word stream, never on text already typed.** The session offers
words to `VoiceCommandProcessor` strictly in spoken order, one at a time, under one gate shared by
the paced loop, a reset's flush and the "transfer everything" button. The matcher answers two
questions — *does what is held match a command?* and *could it still grow into a longer one?* — so
the first half of a two-word command is held for one word instead of being typed and erased. A
command is therefore run exactly once, at its place in the text, and the words around it are never
touched. A held word that is never completed is typed as an ordinary word when the speaker stops.

Buffered output goes through `IDictationOutput` rather than a private buffer, because words are
inserted **at the caret** so the user can direct where they land — and that state belongs to the
view, not to the session.

## Portable settings

`SettingsTransfer` creates version-1 `Cloudict.Settings` and `Cloudict.VoiceCommands` JSON
envelopes. Import validates the complete document on an independent copy before the settings
window stages it; only Save writes it through `SettingsManager` with the existing rollback backup.
Command-only imports replace included language sets and command options while retaining other
languages and application preferences. Initialized collections are replaced during deserialization
so selector defaults do not accumulate. Backups exclude browser profiles and OS permissions.

## Browser provisioning


ChromeDriver must match the installed Chrome's major version, and Chrome updates itself, so "which
driver do we run?" is a moving target. Cloudict 2.x delegated this to `WebDriverManager`, which
downloaded the driver from Google on every startup. That made the app unusable wherever
`storage.googleapis.com` is blocked — it answers **403 Forbidden**, or the TLS handshake is
intercepted — and broke working installs the moment Chrome auto-updated.

`BrowserProvisioner.Resolve()` runs disk-first:

```
detect Chrome (per-platform locations)
        │
        ▼
collect every chromedriver on the machine
  Drivers/ (bundled)  ·  the per-user download cache  ·  system driver locations
  Selenium's cache    ·  PATH
        │
        ├─ one matching Chrome's major version?  ──▶ newest wins. Done, offline.
        │
        ├─ otherwise download it once, into the per-user cache.
        │     mirrors first, Google's host last.
        │
        └─ otherwise use the closest driver available, with the build check disabled.
```

Two properties are deliberate:

- **A driver already on the machine is never overwritten or downgraded.** The bundled driver is a
  floor, not a ceiling.
- **Downloads never write into the install folder**, so provisioning needs no elevation and cannot
  corrupt the shipped copy.

The drivers for all four platforms live in `src/Cloudict.App/Drivers/`; the build copies only the one
matching the target. Refresh them with `scripts/fetch-chromedriver.ps1`.

## Startup

1. `Program.Main` handles the command-line verbs first, because they are how a *second* launch talks
   to the instance already running. `SingleInstance` uses a lock file plus a Unix domain socket —
   both behave identically on all three systems, unlike the Windows-only `Mutex` used in 2.x.
   This is what makes `cloudict --toggle` work, which is the supported way to get a system-wide
   shortcut on Wayland.
2. `AppServices.Initialize()` builds the platform services and the settings store.
3. `LocalizationManager` loads the interface language; English is always kept as the fallback.
4. `MainWindow` constructs the engine, the session and the shortcut registrations.

## Localization

335+ strings live as JSON embedded in `Cloudict.Core`, so one dictionary serves the interface, Core
and the platform layer. XAML resolves them through a `{loc:Tr Key}` markup extension that calls the
same `Loc.Get` API as the code-behind, so there is one lookup path and one fallback rule.

A test asserts the English and Persian sets contain exactly the same keys — a key present in one
language and missing from the other shows up as a raw identifier in the interface, and this catches
it at build time rather than in a screenshot.

Adding a language means dropping in `Strings.<code>.json`, adding the code to
`LocalizationManager.SupportedLanguages`, and adding it to `RightToLeftLanguages` if the script is
right-to-left.

## Notable dependencies

Avalonia (interface), Selenium.WebDriver (drives the helper browser), Newtonsoft.Json (settings),
NAudio (Windows audio routing and microphone diagnostics).

## Microphone health and release (3.2.9)

`DictationSession` checks the page's microphone state once a second through `ISpeechEngine`, on
every platform. Unknown samples and isolated off samples do not stop dictation. Three consecutive
off samples trigger the same serialized flush/reset used for a pause, including a last source-box
read before the flush. The already-transferred word index is retained until the box is seen empty.
If reset cannot restart listening, two start-only retries follow; exhaustion freezes transfer and
asks the UI to stop with a visible explanation. Stop marks the session inactive before awaiting
the loops, so recovery cannot resume typing after a user's Stop.

The UI no longer interprets Windows' machine-wide WASAPI capture state as a reason to stop a
session. That query can report a different device, a mute or another application's stream.

The engine verifies the page really entered its listening state before reporting start success.
Explicit Stop additionally uses Chromium's `Browser.setPermission` to deny microphone capture for
the Google Translate origin in the isolated helper browser. This terminates streams the page's
voice-button state does not account for, without changing source text or other browser sessions.
Explicit Start restores that origin's permission. Pause resets keep the existing permission.
See the [protocol definition](https://chromedevtools.github.io/devtools-protocol/tot/Browser/#method-setPermission).

The voice-command editor exposes the existing modifier flags as independent checkboxes beside a
main-key selector. This avoids invoking a registered global shortcut while defining it. Values
remain in the existing `Ctrl+Alt+S` format, with no settings migration.
