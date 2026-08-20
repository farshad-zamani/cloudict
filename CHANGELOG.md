# Changelog

All notable changes to **Cloudict** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and the
project aims to follow [Semantic Versioning](https://semver.org/).

> **About this project.** Cloudict is a mature application that was developed and refined
> over a long period before being published as free, open-source software. The entries below
> document the public releases.

## [3.0.1] – 2026-08-20

Four things that worked in 2.x and did not survive the move to Avalonia in 3.0.0.

### Fixed
- **The start/stop shortcut needed pressing twice.** `Ctrl+Alt+A` opened the helper browser on the
  first press and only started the microphone on the second. The browser was being reported ready
  as soon as the document finished loading, but Google Translate is a single-page app that builds
  its controls afterwards — the voice button did not exist yet, so activating it failed silently
  and the second press worked only because the page had caught up. Cloudict now waits for the voice
  button itself. One press takes about nine seconds on a cold start, and does the whole job.

### Added
- **The microphone badge is back, redrawn.** A small always-on-top disc in the corner of the screen,
  teal with a microphone while listening and muted red with a slash through it when not. It carries
  the state in both colour and shape rather than colour alone, and it exists because the helper
  browser and whatever you are dictating into normally cover Cloudict's own window. It can be turned
  off in *Settings → General*.
- **Minimize to the system tray**, as a setting again, with the tray icon that goes with it.
  Clicking the icon brings the window back.
- **Desktop notifications when a voice command runs**, using each system's own mechanism: a tray
  balloon on Windows, `notify-send` on Linux, User Notifications on macOS. These have to come from
  the desktop rather than from Cloudict's window, because a voice command fires precisely while you
  are working in another application.
- **`CLOUDICT_DEBUG=1` writes a trace file** next to the logs. `Debug.WriteLine` is compiled out of
  release builds and a windowed application has no console on Windows, so until now there was no way
  to find out why the browser would not come up on someone else's machine.

## [3.0.0] – 2026-08-19

Cloudict now runs on **Linux and macOS** as well as Windows, from one codebase and with one
interface.

### Added
- **Linux support** — `.deb`, `.rpm` and an AppImage. Typing uses X11's XTEST where available and
  `ydotool` on Wayland, which has no injection API of its own. Global shortcuts use `XGrabKey`.
- **macOS support** — signed-ready `.dmg` for Apple Silicon and Intel. Typing uses Quartz events,
  global shortcuts use Carbon hot keys. Builds are currently unsigned; the install notes cover the
  Gatekeeper step and CI switches signing on as soon as credentials exist.
- **`cloudict --diagnose`** prints what the current system supports and where Chrome and the driver
  were found. On Linux "dictation types nothing" nearly always comes down to the session type or a
  missing helper, none of which is visible from the interface.
- **`cloudict --toggle` / `--start` / `--stop`** talk to the running instance. This is the supported
  way to get a system-wide shortcut on Wayland, where no application may claim a global key: bind
  one in the desktop's own settings and point it at `cloudict --toggle`.
- **A cross-platform CI matrix** builds and smoke-tests all three systems on every push, so a change
  that compiles only on Windows fails immediately.

### Changed
- **The interface moved from WPF to Avalonia.** WPF exists only on Windows; one Avalonia
  application now serves all three systems rather than maintaining a second interface per platform.
  The layout, palette and behaviour are unchanged.
- **The code is split into three projects.** `Cloudict.Core` holds the application logic with no UI
  framework and no operating-system calls, `Cloudict.Platform` holds every OS call behind
  interfaces chosen at runtime, and `Cloudict.App` is the interface. Core compiling on its own is
  what stops a platform-specific assumption leaking back in unnoticed.
- **Windows no longer requires administrator rights.** That bought one rare capability — typing
  into a window running as administrator — at the cost of a UAC prompt on every launch, and means
  nothing on Linux or macOS, where permissions govern this instead. Start Cloudict as administrator
  if you need it.
- **Settings moved to the per-user configuration directory** on every platform. The old location
  beside the executable only ever worked because the Windows build ran elevated; it is read-only
  under a Linux prefix and inside a macOS bundle. Existing files are migrated on first run, and the
  Windows installer rescues one from a pre-2.2.6 install before cleaning the folder.
- **Icons are vector rather than emoji.** A Linux system with no emoji font rendered every one of
  them as an empty box, and two were baked into the localized strings themselves.
- **Interface strings moved from WPF resource dictionaries to JSON inside Core**, so the same
  dictionary serves the interface, Core and the platform layer on every system. A test asserts the
  English and Persian sets contain exactly the same keys.
- Upgrading on Windows now clears the install folder first. 3.0 shares almost no file names with
  2.x, so an upgrade previously left the entire WPF runtime behind — 321 MB where 3.0 needs 153.
- Selenium Manager (17 MB of driver-downloading binaries for three platforms) is no longer shipped.
  Cloudict resolves the driver itself and hands Selenium an explicit path, precisely so nothing
  reaches for the network unasked.
- Targets **.NET 10 LTS**, supported to November 2028.

### Fixed
- **The desktop status light wrote to a log file on every microphone poll** — twice a second, about
  173,000 lines a day. On one machine that file had reached 1.4 GB. It now repaints only when the
  state actually changes and writes no log.
- Removed `AngleSharp`, `Polly`, `SharpZipLib`, `Microsoft.Extensions.DependencyInjection`,
  `System.Reactive.Windows.Forms`, `WebDriverManager` and `H.InputSimulator` — all referenced, none
  used.

### Known limitations
- **macOS is built and smoke-tested in CI but has not been run on real hardware.** Treat typing and
  the Accessibility prompt as unverified until someone runs it on a Mac.
- On Wayland without `ydotool`, Cloudict can only type into X11/XWayland windows. It says so rather
  than failing silently.
- macOS builds are unsigned until an Apple Developer Program membership exists.

## [2.3.1] – 2026-08-07

### Added
- **The app version is now visible in the UI** — as a small badge beside the logo in the main
  window and beside the heading in Settings, so it's obvious which build is running without
  digging into file properties. It is read from the assembly, so it can never disagree with the
  build.

### Changed
- **Both windows now fit laptop screens.** Settings opened at 950×1100 with a 700 px minimum
  height — taller than the usable area of a 1366×768 laptop, so its bottom (including the Save
  and Cancel buttons) was pushed off-screen. It now opens at 960×700 with a more compact header,
  and both windows shrink further to fit whatever display they open on.
- **The status bar no longer cuts messages in half.** It was a fixed 35 px single line, so longer
  updates were clipped. It now wraps onto additional lines as needed, keeps the full text in a
  tooltip, and the main window is slightly taller (820 px) to accommodate it.
- **The settings button now reads "Settings"** next to its gear icon instead of being an
  unlabelled square, in both interface languages.
- **The helper-browser button now carries its label inside the button**, next to the globe icon,
  rather than as loose text underneath it.

### Removed
- The **"Select speech engine" button** from the main window. Google Translate is the only active
  engine, and the choice is still available in *Settings → Speech Engine*.

## [2.2.6] – 2026-08-07

### Fixed
- **The browser can no longer fail to start because of a blocked download.** On a fresh install
  the app reported *“Error preparing the browser: The remote server returned an error: (403)
  Forbidden”* (or an SSL/connection error on some networks), and the browser button then failed
  with *“We couldn't prepare the browser.”* The cause was that Cloudict asked WebDriverManager
  to fetch ChromeDriver from Google's host on every startup — and that host
  (`storage.googleapis.com`) answers **403 Forbidden** in a number of regions, while others see
  the TLS handshake intercepted. The installer also deliberately excluded the driver, so a
  first run *always* depended on that download succeeding.

  The same download was re-triggered whenever Chrome auto-updated to a new major version, which
  is why machines that had worked fine for months suddenly broke without anything changing.

- **The installer now ships a ChromeDriver.** The app works offline from the moment it is
  installed — no download, no waiting on a slow connection, nothing to configure.

### Added
- **`BrowserProvisioner`** — resolves Chrome and its driver from disk first, and only touches
  the network as a last resort:
  1. Chrome is located via the Windows *App Paths* registry entries plus the usual install
     folders, so per-user and non-standard installs are found too.
  2. Every ChromeDriver on the machine is collected — the bundled one, Cloudict's own download
     cache, `%LOCALAPPDATA%\ChromeDriver`, Selenium's cache, and `PATH` — and the **newest
     driver matching the installed Chrome wins**. A driver you already had is never overwritten
     or downgraded by the bundled one.
  3. If Chrome is newer than every local driver, the matching driver is fetched **once** into
     `%LOCALAPPDATA%\Cloudict\Drivers` (never the install folder, so no elevation is needed).
     Mirrors are tried **before** Google's host.
  4. If even that is impossible, the closest available driver is used with ChromeDriver's build
     check disabled, so an offline machine still gets a working browser instead of an error.
- **`scripts\fetch-chromedriver.ps1`** — refreshes the bundled driver for a newer Chrome before
  cutting a release.

### Changed
- Failed browser startup no longer dead-ends: the button stays enabled and a click retries the
  whole resolution, so a machine that was offline at launch recovers without a restart.
- Missing Chrome and missing-driver situations now produce an actionable message
  (*“Google Chrome was not found… install it, then press the browser button again”*) instead of
  a raw exception string, in both English and Persian.
- Diagnostics written on a browser failure (`page_debug.html`, `error_screenshot.png`) now go to
  `%LOCALAPPDATA%\Cloudict\Diagnostics` instead of the install folder.
- Dropped the `WebDriverManager` dependency, and stopped shipping Selenium Manager's Linux and
  macOS binaries.

## [2.2.3] – 2026-06-29

### Changed
- **Cleaner main-window layout.** The two box titles (“Recognized text” / “Final text”) are
  now centered and no longer end with a colon, and the action buttons under each box are
  centered within the box.
- **Correct text alignment per interface language.** Text in the Recognized/Final boxes now
  aligns to the right in Persian and to the left in English (previously reversed), following
  the app's UI language.

## [2.2.4] – 2026-06-29

### Fixed
- **No more duplicate / from-scratch re-typing on microphone reset.** Under weak internet, or
  when speech arrived in the final moments before an automatic reset, the app could re-transfer
  everything it had already typed (or leftover text from Google Translate). The reset now:
  1. only fires when a word was actually transferred since the last reset (a pause with no new
     speech no longer triggers a reset that re-sends old text);
  2. **stops observing the Google Translate box first** — no reading, no transfer — before the
     reset begins;
  3. clears Google Translate's box and all word buffers during the reset, so nothing stale can
     be re-read or re-sent, then resumes cleanly.

### Changed
- **New default delays** tuned for reliability: text-processing 600 ms, word-by-word 700 ms
  (transfer-start 2000 ms and reset pause 3500 ms unchanged).
- **Expanded delay explanations** with clearer suggested ranges and explicit **internet-speed**
  guidance (slower/weaker connections need higher values), in English and Persian.

## [2.2.1.1] – 2026-06-29

### Fixed
- **Stop no longer wipes your text when you're using the app locally.** When live transfer
  is *off* (you want to read/copy the text inside Cloudict), pressing Stop now keeps the
  Final-text box intact. In live-transfer mode, the boxes still reset as before.
- **No more false “unsaved changes” prompt.** Closing Settings without changing anything no
  longer asks to confirm — the dirty flag is now a clean baseline after the window loads, so
  the prompt only appears after a real edit.

### Changed
- **Nicer delay tooltips.** Each delay in *Settings → Text Transfer Delays* now has a small
  ⓘ icon next to its title; hovering it opens a clean, rounded, app-font info card with the
  explanation (instead of a long one-line tooltip on the field).

## [2.2.1] – 2026-06-29

### Fixed
- **First two words no longer stick together during live typing.** The leading-space
  decision for each word is now made at send time (reading the up-to-date already-typed
  text), fixing a race where two words dispatched close together were typed with no space
  between them.

### Changed
- **Clearer delay settings.** Each field in *Settings → Text Transfer Delays* now has a
  detailed hover tooltip explaining what it does and *why* it exists (e.g. Google Translate
  revising words as you keep speaking) — in both English and Persian.
- **Documentation.** The README now presents Cloudict as free, Google-powered voice typing
  (like Gboard, but for any Windows app), emphasizes free typing via Google Translate,
  many-language support, and voice commands, and adds a “Tips for best results” section
  (don't minimize the helper browser; tune the delays to stay in sync).

## [2.2.0] – 2026-06-29

### Changed
- Unified the entire app — window titles, assembly metadata, documentation, and the
  published executable (`Cloudict.exe`) — under the **Cloudict** name, with titles in the
  `Cloudict │ …` format.
- Reworded the tagline/description from “any app” to **“anywhere”** (e.g. *“Speak to type
  anywhere”*) in both English and Persian.
- Cleaned the **default Google Translate selectors** so they no longer ship with
  language-specific (Persian) text-box labels; language-agnostic class/auto-detection is now
  the primary path.

### Added
- The personal **website is now a footer button** (globe icon + “Website / وب‌سایت”),
  alongside the GitHub and LinkedIn buttons.
- **In-app help for the Google Translate selectors**: each field now has an inline
  explanation, and a full “how to update these via Inspect Element” guide appears on hover.
- A polished **Windows installer** (Inno Setup) for distributing the final build —
  see [`installer/`](installer/).

### Fixed
- **Automatic microphone reset / re-activation.** The user-configured *Microphone button
  XPath* (Settings → Google Translate) was previously ignored — only built-in structural
  XPaths were tried, which break when Google changes its page and are not reliable across
  dictation languages. The configured selector is now tried **first**, so the microphone can
  be fixed for any language or page change directly from Settings, without recompiling.

## [2.1.0] – 2026

### Added
- Complete **glass / frameless UI redesign** for all windows, with a custom slim scrollbar
  and standardized typography.
- **Bilingual interface** — English by default, Persian selectable — with full RTL/LTR
  support, using the Inter (English) and Vazirmatn (Persian) fonts.
- **Speech-engine selection** scaffold (Google Translate active; others marked “coming
  soon”) and a **typing-language** selector that drives the Google Translate page.
- **Per-language voice commands** (Persian defaults, English defaults, extensible).

## [2.0.0] – 2026

### Changed
- **Open-source preparation:** relicensed under the **MIT License**, removed all
  licensing/activation code, and made the app free. English-first, bilingual documentation
  (`README.md` + `README.fa.md`).

