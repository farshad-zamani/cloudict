# Changelog

All notable changes to **Cloudict** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and the
project aims to follow [Semantic Versioning](https://semver.org/).

> **About this project.** Cloudict is a mature application that was developed and refined
> over a long period before being published as free, open-source software. The entries below
> document the public releases.

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

