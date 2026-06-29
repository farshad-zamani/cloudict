# Changelog

All notable changes to **Cloudict** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and the
project aims to follow [Semantic Versioning](https://semver.org/).

> **About this project.** Cloudict is a mature application that was developed and refined
> over a long period before being published as free, open-source software. The entries below
> document the public releases.

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

