# Cloudict interface review

2026-11-04 · Scope: a light general review and narrow corrections to the existing Avalonia desktop
interface, followed by a polish pass. The established dark palette, fonts, controls and workflow
are retained. The review covers basic layout, readability and keyboard access.

## Result

The interface has a coherent desktop-tool identity: shared color resources, consistent typography,
real English/Persian localization and standard Avalonia controls. It passes this limited review
after the corrections below. It is not a complete accessibility or performance certification.

| Dimension | Score / 4 | Evidence and limits |
|---|---:|---|
| Accessibility | 3 | Essential icon buttons and inputs named; affected contrast pairs corrected; keyboard focus checked. Full Narrator traversal remains untested. |
| Performance | 3 | Existing DataGrid virtualization and background speech processing retained. No new timers or animations. No profiling performed in this pass. |
| Theming | 3 | Shared palette retained; status text separated from button fills. Some existing hard-coded colors remain. |
| Window resizing | 3 | English/Persian main, settings and editor checked at normal and declared minimum sizes. All monitor/DPI combinations remain untested. |
| Implementation integrity | 3 | Small changes to existing layouts and semantic tokens; no replacement visual system. Avalonia has no DOM/CDP target for the web detector. |
| **Total** | **15 / 20** | **Good within the stated review scope.** |

## Verified findings and corrections

1. **P1 — Small windows hid controls.** At 470×560, the main window's final-text controls overlapped
   its footer. At 600×460, the command editor pushed Save below the client area. The main text area
   and editor form now scroll within their available space; footer actions remain visible. The main
   header uses separate columns so Settings and the wordmark do not collide at narrow widths.
2. **P1 — Some text/fill contrast pairs failed 4.5:1.** White text on the old green fill measured
   3.81:1, its hover 3.13:1, and the red hover 3.90:1. The revised green pairs measure **5.45:1** and
   **4.81:1**; red hover **4.57:1**. Success text on the panel uses a separate light green token at
   **7.23:1**. Error text has its own light red token. Disabled controls are not included in these
   text contrast claims.
3. **P2 — Icon-only controls lacked explicit names.** Start, Stop, and the two Copy/Clear pairs
   now have distinct localized accessibility names and tooltips. Recognized/final text fields and
   command phrase/action/value controls are named. The helper-browser button's accessible name
   follows its Open/Close state.
4. **P2 — Custom tab styling did not explicitly show keyboard focus.** Tabs now have a visible
   outline for keyboard focus without changing the selected tab or its meaning.
5. **P2 — Technical selectors inherited Persian direction.** XPath and CSS selector fields now
   edit left-to-right while their Persian labels and surrounding layout remain right-to-left.
   Command validation errors stay in the editor footer, visible even when the form is scrolled.

## Checks

- Real application windows, with isolated sample settings, in English and Persian.
- Normal sizes: main 520×820; settings 960×900 where the work area permits; editor 720×700.
- Minimum sizes: main 470×560; settings 820×520; editor 600×460.
- Main text area scrolled to reach its lower controls; editor scrolled through its form/help while
  Save remained inside the client area. Footer bounds were checked programmatically.
- Voice-command selection/save/reload remained functional after the layout changes.
- Color ratios calculated from the actual shared sRGB palette using relative luminance.
- Source detector output checked as a supplementary scan. It is not a substitute for rendered
  Avalonia review and does not establish native accessibility compliance.

Remaining broader work, if pursued separately: full screen-reader traversal, mixed-DPI monitor
coverage, high-contrast system themes and measured rendering performance. Further reviews can address those specific areas.
