# Cloudict 3.2.13 verification

Date: 2026-11-06. Scope: optional continuous listening, portable settings and voice commands,
recognized-text controls, and refreshed English/Persian screenshots.

- **240 Core tests passed** in Release configuration. New coverage checks manual listening in
  buffered/live modes, final-tail transfer exactly once without clearing the source, no automatic
  pause/continuous-run resets when disabled, and disabling continuous listening during a browser
  reset or start retry. Existing continuous-recovery, Stop, spacing and command regressions remain included.
- Full settings and all saved command languages round-trip without modifying their source.
  Separate command imports preserve unrelated settings/languages, including intentionally empty
  sets and disabled commands. Legacy commands migrate on a copy. Invalid JSON, incompatible
  format/version, duplicate properties, trailing documents, oversized files, invalid settings,
  null commands and unsupported key commands are rejected.
- Saving an imported configuration preserves exact selector lists and writes the previous
  configuration to the existing rollback backup. Deserialization replaces initialized lists,
  preventing default selectors from accumulating on every load.
- Actual Windows English/Persian windows passed minimum-size layout checks: main 470×560,
  settings 820×520 and editor 600×460. The new down-arrow and listening switch have localized
  accessible names. The command grid retains usable height; its actions scroll into view above
  the settings footer. The backup tab scrolls at the minimum size.
- The real settings-window export/import handlers were exercised with a controlled picker result
  and native Windows storage-file streams: export includes unsaved edits without applying them,
  imported main-window options survive Save/reload, invalid/cancelled imports retain the draft,
  and closing without Save discards imported settings. This checks the app's file workflow;
  interactive navigation of the platform's file-picker dialog was not automated.
- **20 real Windows captures** (10 per interface language) were refreshed from isolated example
  settings, including the new Backup tab and updated command controls. No transfer-delay
  screenshot is included. The main, Backup and command layouts were visually reviewed.
- The release workflow builds native Windows, Linux and both macOS architectures, validates the
  Windows installer filename/embedded version, and uploads packages to a draft release before
  public publication.
- Local self-contained win-x64 publishing and Inno Setup compilation succeeded. Both
  `Cloudict.exe --version` and the generated `Cloudict-3.2.13-Setup.exe` metadata report 3.2.13.

Continuous listening defaults on to preserve existing behavior; turning it off is remembered.
Full imports are staged until Save; restart after saving for every imported option to take effect.
Browser profiles and OS permissions are configured separately. Local UI checks were on Windows;
the multi-hour speech and pre-existing dependency limitations below still apply. There was no new
multi-hour microphone endurance run in this update.

---

# Previous verification: Cloudict 3.2.12

Date: 2026-11-04. Scope: intermittent recognition stops, retained microphone capture, initial
spacing in browser editors, multi-modifier voice commands, bilingual documentation and packages.

## Findings and changes

The former Windows UI microphone watch treated three negative machine-wide WASAPI samples as a
reason to call Stop. It did not flush the pending source text or attempt recovery. This is a code
path matching the reported symptoms; no trace from the user's original incidents was available,
so it is not proof that every incident had the same cause. The watch is replaced by page-state
checks in Core and a bounded recovery through the existing serialized flush/reset path.

The engine previously reported a successful Start after clicking even when the page remained off.
Start now verifies the listening state. Explicit Stop also revokes capture permission for the
helper's Google Translate origin, and Start restores it. A browser stream can outlive the site's
button state, so checking that button alone is insufficient to establish resource release.

In a real Chrome contenteditable field, UI Automation reported U+200F and U+200B before the caret
in otherwise empty fields. Both previously required a leading space. The caret probe now walks
past layout markers; the spacing decision also excludes them. Actual whitespace and Persian
U+200C/U+200D joiners keep their existing behavior. This is a limited fix for demonstrated markers,
not a rewrite of caret inference for every VS Code extension. Some contenteditable providers omit
trailing line breaks in their accessibility ranges; that separate case remains unproven here.

The parser and native injectors already supported multiple modifiers. The editor now exposes
independent checkboxes and a main-key selector, avoiding global-shortcut interception while
defining a command. The existing saved format is unchanged.

## Verification performed

- Baseline: 191 tests passed before changes.
- Updated Core suite: **214 tests passed**, including page loss with pending words, recovery before
  speech, transient and unknown states, retry exhaustion, Stop during recovery, no duplicate
  transfer, multi-modifier command execution and invisible-marker spacing.
- A Stop shortcut delivered during a quick-transfer flush prevents both the later text and later
  key commands in that flush from running. Recovery with pending words is tested in both live
  injection and buffered-output modes.
- Windows Release build and self-contained win-x64 publish succeeded. `--version` reports 3.2.12;
  `--diagnose` resolves the bundled 156 driver against installed Chrome 156.
- With a real Google Translate page in Chrome 156 and a synthetic audio device, a `getUserMedia`
  stream was `live` while the page's voice button was off. Stop made its track `ended`; Start
  restored permission to `granted` and the page to listening. Stop preserved test source text.
- Chrome UI Automation probes: empty, direction-marker-only and zero-width-only fields report
  whitespace/start after the change; Persian and Latin word endings still report their actual
  last character. Probes are read-only and do not move the selection or clipboard.
- A native Windows multi-modifier command produced one registered `Ctrl+Alt+Shift+F12` event and
  released its modifiers. The exact `Ctrl+Alt+S` chord is covered through the parser, session and
  real editor. Its native registration was already reserved on the test desktop, so another
  application's reservation was left alone.
- The real English and Persian command editors selected, saved and reloaded `Ctrl+Alt+S` correctly.
  Screenshots were captured from running application windows with isolated sample settings, then
  visually reviewed. No transfer-delay tab is included in the guide.

## Limits and pre-existing findings

No multi-hour reproduction of the user's original incident or visual observation of their Windows
privacy indicator was available. The stream-ending check verifies capture release by the helper;
another application's capture or Windows' delayed indicator refresh is outside that check.
Linux/macOS packages use the repository's existing native GitHub Actions jobs; local interactive
verification was on Windows. System-audio routing behavior and the user's saved settings were not
changed by this update.

The existing build emits NU1510 (redundant Registry reference), CS0067 (an unused notifier event),
and AVLN3001 (views with parameterized constructors). It also reports **NU1903** for the existing
transitive `Tmds.DBus.Protocol` 0.20.0 dependency used by Avalonia's Linux integration. That existing
dependency issue is **not fixed in this release**; no claim of a security-clean dependency audit
is made. See the [advisory and patched versions](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9).

## Rollback

Reinstall 3.2.8 from the existing release if needed. There is no settings schema migration in
3.2.10; existing commands keep the same format. A multi-modifier command is understood by 3.2.8's
existing parser as well. The previous release and tag remain available.


## Interface follow-up

The final public build includes the [interface review](UI-REVIEW.md), normal/minimum-window checks, refreshed captures and the same 214-test regression suite. Intermediate build tags were retained without publishing their releases.


## Installer version check

The Windows installer previously contained a hard-coded 3.2.8 version. It now derives its version from the actual published app host. CI checks both the expected installer filename and its executable version metadata before uploading. Local Inno Setup compilation verifies this independently of the application build.
