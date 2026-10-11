# WindowTabManager implementation plan

## Phases
1. Inspect environment and Win32 feasibility — complete.
2. Implement native services, matching and explicit persistence — complete.
3. Implement tray, control panel and switchers — complete.
4. Build; run logic/native smoke tests and resource measurements — complete (OS-blocked and physical-input cases explicitly documented).
5. Deliver source, executable, limitations and manual test matrix — complete.

## Decisions
- Empty workspace, no repository instructions or existing code.
- C# / WinForms / .NET Framework 4.8, x64, Win32 P/Invoke. Installed framework compiler permits offline builds without an SDK or packages.
- Dedicated keyboard-hook message loop; worker window inventory; UI receives cached snapshots.
- Conservative persistent matching, three saved slots, separate global visibility settings.
- No preview capture, virtual desktop management or startup registration.

## Errors encountered
- dotnet has runtimes but no SDK; use installed Framework compiler.
- git status: directory is not a Git repository; no existing edits to preserve.
- Initial tests used an OS temp folder; sandbox denied file rename. Tests now isolate files beside their workspace report.
- Native fixture was initially hidden by launch flags; explicitly show owned test windows.
- Unprompted test-process foreground acquisition was denied by Windows. Restore succeeds; report this restriction separately from window-monitor geometry tests.
- One apply_patch hunk ordering failed before changes; reapplied in source order.

## Follow-up: native-style previews
- Replace Alt list with DWM thumbnail cards — complete.
- Replace Win+Tab management-form route with borderless grouped preview overlay — complete.
- Keep settings in tray control panel; retain inline group operations in overview — complete.
- Verify thumbnail lifecycle, routing, layout and document preview limitations — complete (actual compositor pixels and physical key gestures remain manual checks).

## Next Step
Revision 4 centered layout executable ready; user can enable it in the tray layout submenu or control panel.

## Follow-up: supplied SVG icon
- SVG preserved, converted to transparent multi-size ICO, embedded in both build paths and applied to tray/forms — complete.
- Running original executable was locked. Built bin-icon/WindowTabManager.exe while preserving the user's active session — complete.

## Follow-up: game / borderless fullscreen does not trigger
- User identified Arknights via GRYPHLINE launcher; shortcut symptoms and elevation remain unknown.
- Track received modifier events, resync after unlock, explicitly raise previews on every opening — implemented.
- Add tray diagnostics for last opening time, source/app elevation, visibility and Z-order API status — implemented.
- Build bin-gamefix and run logic/native preview checks — complete; game and focus stability require manual confirmation.
- Next: user tests game with this build and supplies shortcut diagnostic text if still failing. Do not label game issue fixed from fixture results.

## UI revision requested 2026-10-11
User authorized implementation after collecting items 1–9 and a local Figma draft. User verified both Task Manager and Arknights work when elevated; preserve asInvoker and do not auto-elevate.
1. Independent shortcut switches + saved global mode; first tray click, dark menu, exit without save prompt — complete.
2. Rounded Alt cards/panel with close request buttons; remove overlay headings/instructions — complete.
3. Win grouped/compact layouts and actual translucent backdrop with documented DWM limitations — complete.
4. Refresh light control panel based on readable draft structure — complete.
5. Meaningful logic/native/UI regressions, inspect artifacts, package standalone revision and document actual limitations — complete.

## Follow-up: per-monitor Win+Tab — 2026-10-11
User authorized item 10 with 「做」.
1. Inspect single-overlay lifecycle and monitor assignment — complete.
2. Create one overview per connected monitor; filter local windows and coordinate dismissal/refresh — complete.
3. Verify multi-monitor native previews, shared groups, hidden windows and lifecycle; build separate revision — complete (140 functional assertions pass).
4. Document verified behavior and remaining physical-input/hotplug checks — complete.
- Initial no-activation policy for every monitor blocked target focus in test; preserve normal target activation and use no-activation only for other monitors. Final test passes.
- Performance visible-phase sample ended with hidden panels and zero DWM registrations; no visible multi-monitor rendering budget claimed.
- git diff check detected mixed newline in appended .gitignore entry; normalized LF before delivery.

## Follow-up: optional centered Win+Tab
- Add independent global CenterOverview preference, tray and control-panel switches — complete.
- Center rows and vertically center fitting content within each monitor; preserve scrolling and both layouts — complete.
- Verify legacy defaults, persistence, row alignment, overflow, multi-monitor live previews; package revision 4 — complete (65 logic, 27 UI, 34 monitor assertions).
- Initial overflow test required a 274px card to fit a 220px viewport; corrected fixture height to 400px (320px viewport) to test last-row reachability without an impossible full-card-fit assertion.
