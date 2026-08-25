# Changelog

All notable changes to **Cloudict** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and the
project aims to follow [Semantic Versioning](https://semver.org/).

> **About this project.** Cloudict is a mature application that was developed and refined
> over a long period before being published as free, open-source software. The entries below
> document the public releases.

## [3.0.6] – 2026-08-25

### Fixed
- **A helper browser that failed to load is no longer announced as ready.** Cloudict waited for the
  voice button to appear and then carried on regardless of whether it had — so a page that did not
  load (no network yet just after a reboot, a DNS hiccup, Google returning an error) left a Chrome
  window showing an error that Cloudict reported as ready to dictate into. The load is now checked,
  retried up to three times, and reported honestly when it will not come up.
- **The startup launch waits a moment first.** Starting Chrome in the same breath as the window made
  a cold start the least reliable moment there is; a short pause lets the machine settle.

### Changed
- **The helper-browser button changes colour with its state**: solid red with a globe while the
  browser is closed — something still to be done — and the calm panel colour with a red cross and
  red label once it is open, so it reads as "running, and this is how you close it".

## [3.0.5] – 2026-08-24

### Added
- **The live-transfer choice is remembered between runs.** It is the one control that decides where
  dictation actually goes, and someone who works that way works that way every time — having it come
  back off meant the first sentence of every session went into the wrong place. It is saved the
  moment it is switched, not when Settings is next opened.
- **The helper browser opens by itself when Cloudict starts.** Nothing can be dictated until it is
  open, so making the user press a button first was a step with only one sensible answer. There is a
  checkbox in *Settings → General* for machines where Chrome should not launch unasked.

## [3.0.4] – 2026-08-24

Four fixes found by comparing this build against the 2.x one that had been stable in daily use for
months. Three of them are things that release did better and that did not survive the port.

### Changed
- **The start and stop shortcuts now do one thing each.** `Ctrl+Alt+A` starts and only starts —
  pressing it again while listening does nothing rather than stopping. `Ctrl+Alt+S` stops. The start
  shortcut used to toggle, so its meaning depended on state the user could not see: once Google
  Translate had quietly switched the microphone off, a press was read as "stop", and it took a
  second press to get listening again. There is a dedicated stop shortcut, so start can just start.

### Fixed
- **The source box is now cleared the way the page expects.** Cloudict emptied the textarea by
  setting its value from script, which changes the DOM but leaves Google Translate's own idea of the
  phrase intact — so it could put the whole thing straight back, and the restored text read as new
  speech. The clear now presses the page's own "clear source text" button first, addresses the box
  by the aria-labels the user configured before falling back to guesswork, and raises `change` as
  well as `input`, because some of the page's handlers listen for one and not the other. This is
  what the 2.x build did.
- **The reset no longer rushes the page.** After stopping the microphone, Cloudict waited 200 ms
  before clearing. Google Translate's recognition is often still finishing in that window: it writes
  what it heard a moment later and overwrites the box that was just emptied. The wait is back to the
  ~700 ms the stable build used, which is why the repeat became *more* frequent in 3.x rather than
  less.
- **The voice-command matcher's word history is cleared on every reset and every start.** It keeps a
  short list of recent words so two-word phrases can match; carrying that across a pause let a
  phrase spoken before the pause match again after it. The method to do this was ported in 3.0.0 but
  never called.
- **A silence no longer lets the paced transfer and the flush overlap.** The flush now waits for any
  word already in flight to land before sending the remainder, so the two cannot interleave.

## [3.0.3] – 2026-08-22

### Fixed
- **The start shortcut needed pressing twice after the microphone stopped on its own.** Google
  Translate switches its own microphone off — after a long silence, or when its recognition errors —
  without telling anyone, and the session went on believing it was running. `Ctrl+Alt+A` was then read
  as "stop": it announced that dictation had stopped, did not switch the microphone on, and only the
  second press worked. A session whose microphone the page has already killed is now wound down as
  soon as that is detected, and the start shortcut means start.
- **Voice commands could disappear from the settings window.** They are stored per dictation
  language, but the grid was never reloaded when that language changed, so saving wrote the previous
  language's list — or an empty one — under the newly chosen language. Once an empty Persian set
  existed, the migration that carries pre-3.x commands forward stopped running, and a full set of
  commands became unreachable while still sitting in the settings file. Both halves are fixed, and an
  empty set no longer strands the older list.
- **Text already typed could be sent a second time on the next start.** Whatever Google Translate
  still had in its box when a session ended — most often because it had switched its own microphone
  off mid-dictation — was read on the first poll of the next session and typed again. The box is now
  cleared when dictation starts, and anything that will not clear is recorded as already handled.
- **The logo is legible again.** The full logo carries the app name as artwork, and squeezing that
  into sixty pixels turned the letters into a smear. Only the cloud-and-microphone mark is a picture
  now; "Cloudict" is set as real type, in the two weights the logo uses, so it stays sharp at any size
  and any display scale.

### Added
- **The voice-command editor.** Creating a command has not worked since 3.0.0: the WPF dialog did not
  survive the move to Avalonia, so "Add command" put a row named "new command" into a read-only grid
  with no way to fill it in. There is now a proper editor — phrase, action, and value — reachable from
  "Add command", from "Edit command", and by double-clicking a row.
- **Keys are chosen by pressing them.** A "send key" command took a SendKeys-style code such as `^c`
  or `%{F4}`, which the cross-platform key parser does not read at all, so a command written that way
  was accepted and then silently did nothing. Press the key — with Ctrl, Alt or Shift held if you want
  a combination — and the command records it. The help text has been rewritten to match.

### Changed
- **The selected settings tab is a filled chip** rather than a loose underline that sat unaligned
  under the label.
- **The action column in the commands list is translated.** It showed `TypeText` and `SendKeys` to
  everyone, from a model property that had those names hard-coded in Persian.

## [3.0.2] – 2026-08-20

### Fixed
- **A finished sentence could be typed over and over.** After a long idle period, dictating one
  phrase and then falling silent could set Cloudict typing that same phrase again and again until it
  was stopped by hand. On a silence Cloudict flushes what it has, clears Google Translate's source
  box and restarts the microphone — but the box does not always empty, most often once the page's
  own speech recognition has errored. Cloudict threw away its record of which words had already gone
  out the moment it *asked* for the box to be emptied, so the leftover text read back as brand-new
  speech — and the next silence repeated the whole cycle. That record is now kept until an empty box
  is actually *seen*. Leftover text stays recognised as already handled, and anything genuinely new
  the user says on top of it still goes out, exactly once.
- **Recognition that cannot be restarted now stops cleanly**, with a message, instead of leaving the
  session listening to a microphone that is off.
- **Pressing start during a slow browser launch no longer risks taking Chrome down with it.** The
  helper browser reported itself open the moment Chrome launched, rather than when Google Translate
  had finished loading, so a start arriving in that gap put two threads on one WebDriver session —
  which WebDriver does not support and which can end the session, closing the window. A start now
  waits for the launch to finish instead.
- **The microphone badge follows the microphone, not just the shortcut.** It reported whatever
  Cloudict last asked for, so when Google Translate stopped listening on its own the badge stayed
  green while nothing was being heard. It now also asks the operating system whether the microphone
  is actually being captured. Green means both — dictation running *and* the microphone live — so
  another application holding the microphone never turns it green on its own.
- **The microphone glyph sits in the middle of its disc.** A `Path` scales its geometry to the
  top-left of its own box, and the box was square while the glyph is taller than it is wide, leaving
  the icon a few pixels left of centre.

### Added
- **Desktop notifications when dictation starts and stops**, not only when a voice command runs —
  and one warning per session if the microphone goes quiet underneath you. Start and stop are
  usually driven by the shortcut while another application has focus, which is exactly when
  Cloudict's own status line cannot be seen.

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

