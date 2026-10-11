# Progress

## Centered overview revision 4 — 2026-10-11
- Added independent persistent CenterOverview setting, default off, with tray submenu and control-panel checkboxes. Applies immediately to both layouts on each monitor.
- Rows including short last rows center horizontally; complete fitting content centers vertically in the preview viewport, while tall content stays scrollable. Source HWND positions and groups are unchanged.
- Verified 65 logic, 27 UI, 34 native monitor assertions; reviewed control-panel and centered layout images. Corrected an overflow test whose viewport was smaller than one card; no production defect in that result.
- Built separate bin-centered-v4 executable, preserving older binaries. No resource remeasurement or physical input/game claims.

## Multi-monitor revision 3 — 2026-10-11
- User authorized previously marked item 10. Added per-device overview collection, local filtering in both layouts, coordinated lifecycle, and per-monitor diagnostics. Shared group/visibility model unchanged.
- Display changes dispose all surfaces; next opening reconciles current screens. Non-target panels show without activation; target retains original activation behavior.
- First run passed lifecycle/distribution but foreground assertion was BLOCKED with all panels set to ShowWithoutActivation. Restricted no-activate to passive panels; final target-focus and live transparency tests passed.
- Final functional run: 63 logic + 24 UI + 22 preview + 31 monitor checks pass (140 total). Physical unplug/reconnect, mixed DPI and physical shortcut/game interaction remain manual.
- Viewed portrait/landscape monitor-layout PNGs: correct local-only cards, labels and toolbar boundaries. These DrawToBitmap images do not include DWM pixels.
- Separate bin-multimonitor-v3 output preserves existing versions and settings. Resource measurement: idle 37.20 MiB / CPU 0.156s (~15s), returned 43.43 MiB / CPU 0.109s. Visible-phase sample ended with hidden overlays and zero DWM registrations, explicitly marked inconclusive; no multi-monitor visible performance claim.

- Environment inspected. No existing source files or AGENTS.md found.
- Requirements mapped to native services and explicit persisted configuration slots.
- Implemented native Win32 inventory, event hooks, keyboard worker, persistence, tray and WinForms panels.
- Offline compilation succeeds with no warnings. 37 logic assertions pass, 5 UI assertions pass.
- UI screenshots inspected; adjusted footer height and column labels, fixed test bitmap bounds.
- Actual host has two monitors: portrait DISPLAY1 at (1920,-462), 1080x1920; landscape DISPLAY2 at (0,0), 1920x1080.
- Native tests confirm enumeration, icons, minimization/restore, monitor geometry, current cursor mapping and hook install/removal. Foreground request denied for automated fixture, so active-foreground policy cannot yet be fully verified.
- Final verification: 40 logic assertions, 20 native API assertions and 5 UI assertions pass. Native report contains explicit BLOCKED items for denied foreground acquisition and cursor movement.
- Real external-process fixture verifies event-driven creation, minimization, renaming, additional window, and closure.
- Fixed stale group references after profile save, duplicate-rule accumulation, preserving manual moves when a window closes before save, and rapid-chord generation races.
- Final real-app resource sample: 15-second phases; tray CPU 0.016s/33.10 MiB working set; visible panel CPU 0.359s/41.03 MiB; returned tray CPU 0.047s/41.69 MiB. These are local measurements, not guarantees.
- README.md and TESTING.md include build/run instructions, matching behavior, explicit limitations, API sources and full manual acceptance matrix. verify.ps1 provides repeatable checks.
- Inspected control/overview/Alt rendered PNGs. No test processes remain running. Source and bin/WindowTabManager.exe are ready for delivery.

## Follow-up: preview switchers
- User clarified both switchers need native-style previews and Win+Tab must not open the management app window.
- Added Previews.cs: DWM live thumbnails, aspect-fit/clipped viewport, borderless grouped overview, centered Alt thumbnail grid, keyboard selection and contextual group editing.
- Win+Tab now constructs OverviewSwitcher and hides any existing control panel. Test confirms it does not even construct ManagerForm.
- Thumbnail registrations are released on hide/dispose, exclusion, and scrolling offscreen. No background capture loop or preview timer.
- 42 logic assertions, 10 UI assertions and 14 native preview assertions pass, including a real source in a separate process. Re-rendered and inspected overlay layout PNGs; these do not contain DWM-composited pixels.
- Initial preview resource run ended with zero registered thumbnails; investigate visibility/API results rather than claim it measures live-composition cost.
- Added resource diagnostics: follow-up run ended with overview Visible=False and no registered thumbnails, so the report explicitly marks live-preview cost BLOCKED/inconclusive. This is not reported as a measured preview-rendering budget.
- Cross-process DWM registration/update returned HRESULT 0 for the controlled native fixture. Layout screenshots remain explicitly separate from compositor-pixel validation.
- Fixed a temporary compiler warning/name-replacement error while renaming the paint text helper. Final build passes with no warnings.
- Follow-up complete: native-style overlays, contextual grouping, visible-only thumbnail lifecycle, updated build and README/TESTING documentation. Test processes have exited.

## Follow-up: supplied app icon
- Preserved the user's SVG as assets/WindowTabManager.svg; generated transparent ICO at 16, 20, 24, 32, 48, 64, 128 and 256px using a reproducible WPF conversion script.
- Embedded the icon into executable resources and wired tray/form icons to it. External windows retain their own application icons.
- Default build hit Windows file locking because the user is running the previous executable. Preserve that session and build the updated version in bin-icon instead; no running app was terminated.

## Game compatibility follow-up
- User identified Arknights launched through GRYPHLINE. No matching running process was returned by the available shell; actual game/elevation were not inspected.
- Added event-based modifier tracking, unlock resync, explicit per-open topmost positioning, and copyable tray diagnostics (last Alt/Win opening only, no ordinary key logging).
- Built bin-gamefix independently; logic 50 pass. Native preview final run 19 pass. Earlier focus/Z-order assertions were unstable; TESTING.md retains this limitation. Game input, focus and visual occlusion need physical testing.
- Two documentation patch batches failed validation before changing any files; corrected and reapplied.

## UI revision 2 — 2026-10-11
- Implemented all nine collected changes, including a light control panel using the readable draft structure; dark tray/context menus; independent persistent shortcut switches; explicit first-menu cancellation fix; exit without saving dirty groups.
- Added native WM_CLOSE buttons (no process termination), rounded window/card geometry, heading/help removal, grouped/compact overview, and a translucent backdrop separate from opaque DWM cards.
- Diagnosed previous Alt focus instability using WM_ACTIVATE stack: WinForms Form.TopMost setter during CreateHandle used SetWindowPos with activation. Replaced managed TopMost with native style plus explicit NOACTIVATE positioning; fixed mouse-activation handling.
- Full-screen DWM Acrylic request alone returned S_OK but produced opaque gray pixels in this environment. Split backdrop at 0.64 opacity from region-clipped opaque card surface. Actual composited RGB changed with controlled source; blur parity is explicitly not claimed.
- 63 logic + 28 native + 24 UI + 22 preview checks passed. Native cursor repositioning remains BLOCKED. First tray Opening path verified; physical shell gestures remain manual.
- Performance: 3 sources; idle 36.77 MiB, overview 40.44 MiB (3 active DWM thumbnails), returned 40.68 MiB (0 thumbnails). Process CPU 0.016/0.016/0.000 seconds over ~15s phases; excludes DWM/GPU.
- Initial intermediate bin-ui-v2 rebuild encountered a file lock without a visible matching process; used bin-preview-v2. Existing bin/bin-icon/bin-gamefix preserved. Default script/MSBuild output now bin-preview-v2.
- User had confirmed both Task Manager and Arknights work elevated; manifest remains asInvoker and no auto-elevation was added.
