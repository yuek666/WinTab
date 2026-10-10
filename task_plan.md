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
Updated executable and documentation ready. Manual confirmation of native-composited pixels and physical shortcuts is still explicitly documented.

## Follow-up: supplied SVG icon
- SVG preserved, converted to transparent multi-size ICO, embedded in both build paths and applied to tray/forms — complete.
- Running original executable was locked. Built bin-icon/WindowTabManager.exe while preserving the user's active session — complete.

## Follow-up: game / borderless fullscreen does not trigger
- User identified Arknights via GRYPHLINE launcher; shortcut symptoms and elevation remain unknown.
- Track received modifier events, resync after unlock, explicitly raise previews on every opening — implemented.
- Add tray diagnostics for last opening time, source/app elevation, visibility and Z-order API status — implemented.
- Build bin-gamefix and run logic/native preview checks — complete; game and focus stability require manual confirmation.
- Next: user tests game with this build and supplies shortcut diagnostic text if still failing. Do not label game issue fixed from fixture results.
