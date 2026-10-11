using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WindowTabManager
{
    internal static class Tests
    {
        private static readonly List<string> log = new List<string>();
        private static int failures;
        private static void Check(bool condition, string name) { log.Add((condition ? "PASS " : "FAIL ") + name); if (!condition) failures++; }
        private static WindowInfo Window(string key, string title, string exe, string monitor)
        {
            return new WindowInfo { Key = key, Handle = new IntPtr(key.GetHashCode()), Title = title, Executable = exe, ClassName = "TestWindow", Monitor = monitor, AppName = Path.GetFileNameWithoutExtension(exe) };
        }
        private static int Finish(string path)
        {
            log.Add("Failures: " + failures);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllLines(path, log); return failures == 0 ? 0 : 1;
        }
        public static int Run(string path)
        {
            log.Clear(); failures = 0;
            string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "self-test-data-" + Guid.NewGuid().ToString("N"));
            try {
                var a = Window("a", "Document A", "C:\\Editor.exe", "DISPLAY1");
                var b = Window("b", "Document B", "C:\\Editor.exe", "DISPLAY2");
                var c = Window("c", "Browser", "C:\\Browser.exe", "DISPLAY1");
                var rules = new[] { WindowRule.From(a, "work"), WindowRule.From(b, "other") };
                Check(Matcher.Match(new[] { a, b }, rules)[a.Key].GroupId == "work", "same app: distinct exact titles map correctly");
                a.Title = "Changed";
                Check(!Matcher.Match(new[] { a, b }, rules).ContainsKey(a.Key), "multiple live/saved windows: changed title stays ungrouped");
                Check(Matcher.Match(new[] { a }, new[] { rules[0] }).ContainsKey(a.Key), "one-to-one family fallback survives title change");
                b.Title = a.Title;
                Check(Matcher.Match(new[] { a, b }, new[] { WindowRule.From(a, "g") }).Count == 0, "identical live titles never guessed");
                Check(Matcher.Match(new[] { a }, new[] { WindowRule.From(a, "g"), WindowRule.From(a, "h") }).Count == 0, "conflicting saved rules never guessed");
                var restricted = Window("restricted", "Private", "", "DISPLAY1");
                Check(Matcher.Match(new[] { restricted }, new[] { WindowRule.From(restricted, "g") }).Count == 0, "missing executable identity cannot persistently match");
                a.Title = "Document A"; b.Title = "Document B";
                var settings = new Settings(); var workspace = new Workspace(settings); workspace.Update(new[] { a, b, c }); workspace.AutoGroup();
                Check(workspace.Current.Groups.Count == 2 && workspace.GroupOf(a) == workspace.GroupOf(b), "application grouping spans monitors");
                var group = workspace.AddGroup("Manual"); workspace.Move(b, group.Id);
                Check(workspace.GroupOf(b) == group.Id && workspace.Dirty, "manual edits after preset take effect immediately");
                var p = workspace.Capture("First"); workspace.AcceptSaved(0, p);
                workspace.Move(a, group.Id);
                Check(settings.Slots[0].Rules.Single(r => r.Title == a.Title).GroupId != workspace.GroupOf(a), "unsaved assignments do not mutate stored profile");
                workspace.Current.Groups[0].Name = "Changed group";
                Check(settings.Slots[0].Groups[0].Name != "Changed group", "loaded groups are deep copies");
                workspace.DeleteGroup(group);
                Check(workspace.GroupOf(a) == null && workspace.GroupOf(b) == null && workspace.Windows.Length == 3 && !workspace.Current.Groups.Any(g => g.Id == group.Id), "delete group moves windows to ungrouped without closing");
                workspace.SetVisibility(a, true, true);
                Check(workspace.Hidden(a, true) && !workspace.Hidden(a, false), "Alt and Win exclusions independent");
                Check(workspace.Windows.Contains(a), "hidden window remains in control inventory");
                workspace.SetVisibility(a, true, false);
                Check(!workspace.Hidden(a, true), "unhide restores candidacy");
                var snapshot = workspace.Windows.Where(w => w.Monitor == "DISPLAY1").ToArray();
                var newWindow = Window("new", "New", "C:\\Other.exe", "DISPLAY2");
                workspace.Update(new[] { a, b, c, newWindow });
                Check(snapshot.Length == 2 && snapshot.All(w => w.Monitor == "DISPLAY1"), "captured monitor candidate list remains fixed after inventory changes");
                Check(workspace.GroupOf(newWindow) == null, "unmatched new windows default to ungrouped");
                workspace.Load(0); a.Title = "Renamed live"; workspace.Update(new[] { a, b, c });
                Check(workspace.GroupOf(a) == p.Rules[0].GroupId, "same live identity retains group across title changes");
                for (int i = 0; i < 3; i++) { var profile = workspace.Capture("Slot " + i); settings.Slots[i] = profile; }
                settings.StartupSlot = 2; settings.UseActiveMonitor = true;
                workspace.SetVisibility(b, false, true);
                Directory.CreateDirectory(temp); string file = Path.Combine(temp, "settings.json"); JsonStore.Save(file, settings);
                var restored = JsonStore.Load(file);
                Check(restored.Slots.Length == 3 && restored.Slots[2].Name == "Slot 2" && restored.StartupSlot == 2, "all three slots and startup selection round-trip to disk");
                var restarted = new Workspace(restored); restarted.Update(new[] { a, b }); restarted.Load(2);
                Check(restarted.GroupOf(b) == workspace.GroupOf(b) && restarted.Hidden(b, false), "restart restores grouping and independent global visibility");
                Check(restored.UseActiveMonitor, "global monitor policy round-trips");
                string json = File.ReadAllText(file);
                Check(!json.Contains("Handle") && !json.Contains("ProcessId"), "persistent file contains no HWND or transient process ID");
                JsonStore.Save(file, restored); Check(File.Exists(file + ".bak"), "atomic replacement preserves backup");
                File.WriteAllText(file, "invalid"); bool rejected = false; try { JsonStore.Load(file); } catch { rejected = true; }
                Check(rejected, "corrupt settings rejected instead of silently overwritten");
                var absent = new Workspace(new Settings()); absent.Update(new[] { a }); absent.AutoGroup();
                var absentProfile = absent.Capture("Absent"); absent.Settings.Slots[0] = absentProfile; absent.Load(0); absent.Update(new WindowInfo[0]);
                Check(absent.Capture("Absent").Rules.Count == 1, "saving while window absent retains its existing rule");
                absent.Update(new[] { a }); var movedGroup = absent.AddGroup("After close"); absent.Move(a, movedGroup.Id); absent.Update(new WindowInfo[0]);
                Check(absent.Capture("After close").Rules.Single().GroupId == movedGroup.Id, "manual move survives window closing before explicit save");
                var duplicates = new Workspace(new Settings()); b.Title = a.Title; duplicates.Update(new[] { a, b }); duplicates.AutoGroup();
                duplicates.AcceptSaved(0, duplicates.Capture("Duplicates")); duplicates.Load(0);
                Check(duplicates.Capture("Duplicates again").Rules.Count == 2, "saving ambiguous duplicate titles does not multiply persisted rules");
                b.Title = "Document B";

                string legacyPath = Path.Combine(temp, "legacy-settings.json");
                File.WriteAllText(legacyPath, "{\"Version\":1,\"StartupSlot\":-1,\"UseActiveMonitor\":false,\"Slots\":[null,null,null],\"Visibility\":[]}");
                var legacy = JsonStore.Load(legacyPath);
                Check(!legacy.PauseAlt && !legacy.PauseWin && !legacy.CompactOverview, "old settings migrate with both shortcuts enabled and grouped layout");
                legacy.PauseAlt = true; legacy.CompactOverview = true; JsonStore.Save(legacyPath, legacy);
                var revised = JsonStore.Load(legacyPath);
                Check(revised.PauseAlt && !revised.PauseWin && revised.CompactOverview, "independent shortcut preference and overview layout survive restart");
                for (int mask = 0; mask < 4; mask++) {
                    var configured = new ShortcutState(); bool enabledAlt = (mask & 1) != 0, enabledWin = (mask & 2) != 0;
                    var altDecision = configured.Handle(9, true, true, false, false, false, enabledAlt, enabledWin);
                    Check(altDecision.Swallow == enabledAlt && altDecision.MaskMenu == enabledAlt && (altDecision.Action == SwitchAction.BeginAlt) == enabledAlt, "Alt interception respects independent enabled mask " + mask);
                    configured.Reset();
                    var winDecision = configured.Handle(9, true, false, true, false, false, enabledAlt, enabledWin);
                    Check(winDecision.Swallow == enabledWin && winDecision.MaskMenu == enabledWin && (winDecision.Action == SwitchAction.Overview) == enabledWin, "Win interception respects independent enabled mask " + mask);
                }
                var pausedChord = new ShortcutState(); pausedChord.Handle(9, true, true, false, false, false);
                Check(pausedChord.Handle(9, false, true, false, false, false, false, true).Swallow, "pausing mid-chord still consumes the already-intercepted Tab release");
                Check(pausedChord.Handle(0xA4, false, false, false, false, false, false, true).Action == SwitchAction.None, "pausing Alt cancels pending commit");
                using (var round = SurfaceStyle.Round(new Rectangle(0, 0, 120, 80), 16))
                    Check(!round.IsVisible(0, 0) && round.IsVisible(60, 40), "rounded outline excludes square corner and retains center");

                var modifiers = new ModifierState();
                modifiers.Seed(k => false);
                modifiers.Observe(0x5B, true);
                var hookOrder = new ShortcutState();
                Check(hookOrder.Handle(9, true, modifiers.Alt, modifiers.Win, modifiers.Shift, modifiers.Control).Action == SwitchAction.Overview,
                    "observed Win down triggers overview even when async state was not updated by a downstream hook");
                modifiers.Observe(0x5C, true); modifiers.Observe(0x5B, false);
                Check(modifiers.Win, "releasing left Win preserves held right Win");
                modifiers.Observe(0x5C, false); Check(!modifiers.Win, "last Win release clears tracked modifier");
                modifiers.Observe(0xA4, true); modifiers.Observe(0xA5, true); modifiers.Observe(0xA4, false);
                Check(modifiers.AltFor(0xA4, false, 0x20), "one Alt release preserves opposite Alt even with context flag");
                modifiers.Observe(0xA5, false);
                Check(!modifiers.AltFor(0xA5, false, 0x20), "last Alt release ignores pre-release context flag");
                Check(modifiers.AltFor(9, true, 0x20), "Tab Alt context recovers a missing Alt down event");
                modifiers.Observe(0xA1, true); modifiers.Observe(0xA3, true);
                Check(modifiers.Shift && modifiers.Control, "right Shift and Ctrl are tracked for reverse and pass-through");
                modifiers.Seed(k => k == 0xA4);
                Check(modifiers.Alt && !modifiers.Win && !modifiers.Shift && !modifiers.Control, "resume seed clears stale keys and restores currently held Alt");
                var key = new ShortcutState();
                Check(key.Handle(9, true, true, false, false, false).Action == SwitchAction.BeginAlt, "Alt+Tab starts session");
                Check(key.Handle(9, false, true, false, false, false).Swallow, "consumed Tab keyup suppressed");
                Check(key.Handle(9, true, true, false, true, false).Action == SwitchAction.StepBackward, "Alt+Shift+Tab reverses");
                Check(key.Handle(0xA4, false, false, false, false, false).Action == SwitchAction.CommitAlt, "last Alt release commits");
                key.Reset(); key.Handle(9, true, true, false, false, false);
                Check(key.Handle(0xA4, false, true, false, false, false).Action == SwitchAction.None, "releasing one of two held Alt keys does not commit");
                Check(key.Handle(27, true, true, false, false, false).Action == SwitchAction.Cancel, "Escape cancels selection");
                Check(key.Handle(0xA4, false, false, false, false, false).Action == SwitchAction.None, "Alt release after cancellation does not commit");
                key.Reset(); var overview = key.Handle(9, true, false, true, false, false);
                Check(overview.Swallow && overview.Action == SwitchAction.Overview && overview.MaskMenu, "Win+Tab requests persistent overview and menu masking");
                Check(key.Handle(0x5B, false, false, false, false, false).Action == SwitchAction.None && key.OverviewOpen, "overview survives Win release");
                Check(key.Handle(9, true, false, false, false, false).Action == SwitchAction.None, "ordinary Tab stays available for overview UI navigation");
                key.Reset(); Check(!key.Handle(9, true, true, false, false, true).Swallow, "Ctrl+Alt+Tab passes through");
                key.Handle(9, true, true, false, false, false); key.ClosePanel();
                Check(key.Handle(0xA4, false, false, false, false, false).Action == SwitchAction.None, "mouse choice clears pending Alt commit");
                key.Reset(); key.Handle(9, true, true, false, false, false); int previousGeneration = key.Generation;
                key.Handle(0xA4, false, false, false, false, false); key.Handle(9, true, true, false, false, false);
                key.ClosePanel(previousGeneration);
                Check(key.AltSession, "late UI close from prior chord cannot cancel a newer chord");
                Check(AltSwitcher.InitialIndex(3, 0, false) == 1 && AltSwitcher.InitialIndex(3, 0, true) == 2 && AltSwitcher.InitialIndex(0, -1, false) == -1, "initial MRU choice, reverse wrapping, empty list");
                Check(LiveThumbnail.Fit(new Size(1600, 900), new Rectangle(0, 0, 320, 240)) == new Rectangle(0, 30, 320, 180), "wide live preview preserves aspect ratio with letterboxing");
                Check(LiveThumbnail.Fit(new Size(600, 1200), new Rectangle(0, 0, 300, 200)) == new Rectangle(100, 0, 100, 200), "portrait preview is fitted without stretching");
            } catch (Exception e) { failures++; log.Add(e.ToString()); }
            finally {
                string root = Path.GetDirectoryName(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(temp).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(temp)) Directory.Delete(temp, true);
            }
            return Finish(path);
        }
        public static int NativeRun(string path)
        {
            log.Clear(); failures = 0; IntPtr original = Native.GetForegroundWindow();
            try {
                log.Add("OS: " + Environment.OSVersion + "; process x64: " + Environment.Is64BitProcess);
                log.Add("Screens: " + Screen.AllScreens.Length);
                foreach (var s in Screen.AllScreens) log.Add(s.DeviceName + " " + s.Bounds);
                var desktop = Native.Enumerate(new Dictionary<IntPtr, long>(), false);
                log.Add("External manageable windows: " + desktop.Length);
                Check(desktop.All(w => !String.IsNullOrWhiteSpace(w.Title) && w.Monitor != null), "real desktop enumeration returns titled monitor-associated windows");
                Check(System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Input)) == 40, "native INPUT layout is x64-correct");
                using (var first = new Form { Text = "WTM native test A", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(Screen.PrimaryScreen.WorkingArea.Left + 80, Screen.PrimaryScreen.WorkingArea.Top + 80, 350, 220) })
                using (var second = new Form { Text = "WTM native test B", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(Screen.PrimaryScreen.WorkingArea.Left + 150, Screen.PrimaryScreen.WorkingArea.Top + 150, 350, 220) }) {
                    first.Show(); second.Show();
                    Native.ShowWindowAsync(first.Handle, 5); Native.ShowWindowAsync(second.Handle, 5); Pump(200);
                    var found = Native.Enumerate(new Dictionary<IntPtr, long>(), true);
                    var a = found.Single(w => w.Handle == first.Handle); var b = found.Single(w => w.Handle == second.Handle);
                    Check(a.Executable.EndsWith("WindowTabManager.exe", StringComparison.OrdinalIgnoreCase), "QueryFullProcessImageName resolves actual fixture executable");
                    Check(Native.StillSame(a), "activation identity validates HWND/process/start time/class");
                    using (var icon = Native.ReadIcon(first.Handle)) Check(icon.Width > 0, "native icon extraction works");
                    first.WindowState = FormWindowState.Minimized; Pump(150);
                    Check(Native.Enumerate(new Dictionary<IntPtr, long>(), true).Single(w => w.Handle == first.Handle).Minimized, "minimized window remains enumerable");
                    bool activated = Native.Activate(a); Pump(250);
                    Check(!Native.IsIconic(first.Handle), "ShowWindowAsync restores minimized fixture");
                    if (activated && Native.GetForegroundWindow() == first.Handle) Check(true, "foreground switching succeeds for normal test window");
                    else log.Add("BLOCKED: Windows denied automated foreground acquisition. Restore succeeded; real user-triggered activation remains to be tested.");
                    foreach (var screen in Screen.AllScreens) {
                        first.Bounds = new Rectangle(screen.WorkingArea.Left + 50, screen.WorkingArea.Top + 50, 340, 210); Pump(100);
                        Check(Native.MonitorOf(first.Handle) == screen.DeviceName, "native window monitor mapping: " + screen.DeviceName);
                        Native.SetForegroundWindow(first.Handle); Pump(50);
                        if (Native.GetForegroundWindow() == first.Handle) Check(Native.TargetMonitor(true).Device == screen.DeviceName, "active-window monitor policy: " + screen.DeviceName);
                        else log.Add("BLOCKED: could not make fixture foreground on " + screen.DeviceName + "; active-window policy requires manual confirmation there.");
                    }
                    Native.Point mouse; Native.GetCursorPos(out mouse);
                    Check(Native.TargetMonitor(false).Device == Screen.FromPoint(new Point(mouse.X, mouse.Y)).DeviceName, "current mouse monitor policy agrees with screen geometry");
                    try {
                        foreach (var screen in Screen.AllScreens) {
                            if (Native.SetCursorPos(screen.WorkingArea.Left + 60, screen.WorkingArea.Top + 60))
                                Check(Native.TargetMonitor(false).Device == screen.DeviceName, "cursor monitor policy on " + screen.DeviceName);
                            else log.Add("BLOCKED: SetCursorPos denied on " + screen.DeviceName);
                        }
                    } finally { Native.SetCursorPos(mouse.X, mouse.Y); }
                    if (Screen.AllScreens.Length >= 2) {
                        var left = Screen.AllScreens.OrderBy(s => s.Bounds.Left).First();
                        first.Bounds = new Rectangle(left.Bounds.Right - 220, Math.Max(50, left.Bounds.Top + 50), 750, 350); Pump(100);
                        var largest = Screen.AllScreens.OrderByDescending(s => { var r = Rectangle.Intersect(s.Bounds, first.Bounds); return (long)r.Width * r.Height; }).First();
                        Check(Native.MonitorOf(first.Handle) == largest.DeviceName, "spanning window follows monitor with largest intersection");
                    }
                    IntPtr closed = second.Handle; second.Close(); Pump(100);
                    Check(!Native.Enumerate(new Dictionary<IntPtr, long>(), true).Any(w => w.Handle == closed) && !Native.StillSame(b), "closed window removed and stale activation rejected");
                }
                using (var hook = new KeyboardHook(c => { }, s => log.Add("HOOK WARNING " + s))) { Check(true, "real WH_KEYBOARD_LL installation succeeds (no synthetic shortcut claimed)"); }
                Check(true, "hook disposal and worker shutdown return successfully");
                using (var closable = new Form { Text = "WTM safe close fixture", Size = new Size(320, 180) }) {
                    bool refuse = true; int requests = 0;
                    closable.FormClosing += delegate(object sender, FormClosingEventArgs e) { requests++; e.Cancel = refuse; };
                    closable.Show(); Native.ShowWindowAsync(closable.Handle, 5); Pump(100);
                    var live = Native.Enumerate(new Dictionary<IntPtr, long>(), true).Single(w => w.Handle == closable.Handle);
                    Check(Native.RequestClose(live), "WM_CLOSE request is posted to real window"); Pump(100);
                    Check(requests == 1 && !closable.IsDisposed, "application can cancel close to preserve unsaved work");
                    string key = live.Key; live.Key = "stale";
                    Check(!Native.RequestClose(live), "close request rejects stale identity"); live.Key = key;
                    refuse = false; Check(Native.RequestClose(live), "accepted close request is posted"); Pump(100);
                    Check(closable.IsDisposed, "normal close destroys fixture without terminating its process");
                }
                var snapshots = new ConcurrentQueue<WindowInfo[]>();
                var errors = new ConcurrentQueue<string>();
                using (var inventory = new WindowInventory(w => snapshots.Enqueue(w), s => errors.Enqueue(s)))
                using (var fixture = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--fixture") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) {
                    var wait = Stopwatch.StartNew();
                    while (!fixture.HasExited && wait.ElapsedMilliseconds < 10000) Pump(100);
                    if (!fixture.HasExited) { fixture.Kill(); Check(false, "test fixture exits within 10 seconds"); }
                    Pump(450);
                    var captured = snapshots.Select(s => s.Where(w => w.ProcessId == (uint)fixture.Id).ToArray()).ToArray();
                    Check(captured.Any(s => s.Any(w => w.Title == "WTM event A")), "event inventory discovers new external-process window");
                    Check(captured.Any(s => s.Any(w => w.Minimized)), "event inventory observes minimization");
                    Check(captured.Any(s => s.Any(w => w.Title == "WTM event renamed")), "event inventory observes title change");
                    Check(captured.Any(s => s.Length == 2), "event inventory adds second same-program window");
                    Check(captured.Last().Length == 0, "event inventory removes closed process windows");
                    Check(errors.IsEmpty, "event hooks registered without fallback warnings");
                }
                log.Add("MANUAL REQUIRED: physical Alt/Win chords, native fallback after pause/exit, cross-monitor cursor movement during a held chord, hot unplug, elevated/secure desktop.");
            } catch (Exception e) { failures++; log.Add(e.ToString()); }
            finally { Native.SetForegroundWindow(original); }
            return Finish(path);
        }
        internal static void Pump(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
        }
        public static int FixtureRun()
        {
            using (var first = new Form { Text = "WTM event A", Size = new Size(340, 200), StartPosition = FormStartPosition.CenterScreen })
            using (var second = new Form { Text = "WTM event B", Size = new Size(340, 200), StartPosition = FormStartPosition.CenterScreen })
            using (var timer = new System.Windows.Forms.Timer { Interval = 1000 }) {
                first.Show(); Native.ShowWindowAsync(first.Handle, 5);
                int step = 0;
                timer.Tick += delegate {
                    step++;
                    if (step == 1) first.WindowState = FormWindowState.Minimized;
                    if (step == 2) { first.WindowState = FormWindowState.Normal; first.Text = "WTM event renamed"; }
                    if (step == 3) { second.Show(); Native.ShowWindowAsync(second.Handle, 5); }
                    if (step == 4) first.Close();
                    if (step == 5) { second.Close(); timer.Stop(); Application.ExitThread(); }
                };
                timer.Start(); Application.Run();
            }
            return 0;
        }
        public static int PerfRun(string directory)
        {
            log.Clear(); failures = 0; Directory.CreateDirectory(directory);
            log.Add("Local measurement; not a guaranteed budget. Physical desktop activity influences results.");
            log.Add("Logical processors: " + Environment.ProcessorCount);
            try {
                using (var app = new AppController(Path.Combine(directory, "performance-settings.json"), false))
                using (var timer = new System.Windows.Forms.Timer { Interval = 15000 }) {
                    var process = Process.GetCurrentProcess();
                    var watch = Stopwatch.StartNew(); TimeSpan cpu = process.TotalProcessorTime;
                    int phase = 0;
                    timer.Tick += delegate {
                        process.Refresh(); double seconds = watch.Elapsed.TotalSeconds;
                        double cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
                        log.Add(String.Format("{0}: duration={1:F2}s; CPU={2:F3}s; one-core={3:F3}%; total-capacity={4:F3}%; working-set={5:F2} MiB; private={6:F2} MiB; windows={7}; state={8}; DWM registrations={9}",
                            phase == 0 ? "Tray idle (includes startup)" : phase == 1 ? "Win+Tab live preview visible" : "Returned to tray",
                            seconds, cpuSeconds, cpuSeconds / seconds * 100, cpuSeconds / seconds / Environment.ProcessorCount * 100,
                            process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0, app.Model.Windows.Length, app.Status, LiveThumbnail.ActiveCount));
                        if (phase == 0) { Check(app.ControlPanel == null, "normal startup stays tray-only"); app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = Native.TargetMonitor(false) }); }
                        else if (phase == 1) {
                            log.Add("Overview visible=" + app.OverviewPanel.Visible + "; cards=" + app.OverviewPanel.CardCount + "; thumbnail HRESULTs=" + app.OverviewPanel.ThumbnailResults);
                            if (!app.OverviewPanel.Visible || app.OverviewPanel.PreviewCount == 0) log.Add("BLOCKED: no active preview at sample end; this sample does not establish live-preview rendering cost.");
                            app.OnCommand(new SwitchCommand { Action = SwitchAction.Cancel }); Check(LiveThumbnail.ActiveCount == 0, "closing measured overview releases all live previews");
                        }
                        else { timer.Stop(); app.ExitThread(); return; }
                        phase++; cpu = process.TotalProcessorTime; watch.Restart();
                    };
                    timer.Start(); Application.Run(app);
                }
            } catch (Exception e) { failures++; log.Add(e.ToString()); }
            return Finish(Path.Combine(directory, "performance.txt"));
        }
        private static bool Above(IntPtr upper, IntPtr lower)
        {
            for (IntPtr hwnd = Native.GetWindow(upper, 2); hwnd != IntPtr.Zero; hwnd = Native.GetWindow(hwnd, 2))
                if (hwnd == lower) return true;
            return false;
        }
        public static int PreviewRun(string directory)
        {
            log.Clear(); failures = 0; Directory.CreateDirectory(directory);
            try {
                using (var first = new Form { Text = "DWM preview — document", Size = new Size(760, 470), BackColor = Color.FromArgb(243, 238, 222) })
                using (var second = new Form { Text = "DWM preview — dashboard", Size = new Size(620, 420), BackColor = Color.FromArgb(32, 87, 107) })
                using (var app = new AppController(Path.Combine(directory, "preview-test-settings.json"), true)) {
                    first.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "WindowTabManager\n\nLive document preview\nThis is a real source window.", Font = new Font("Segoe UI", 24), Padding = new Padding(35) });
                    second.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "LIVE DASHBOARD\n\n128  /  256  /  512", Font = new Font("Segoe UI", 24), ForeColor = Color.White, Padding = new Padding(30) });
                    first.Show(); second.Show(); Native.ShowWindowAsync(first.Handle, 5); Native.ShowWindowAsync(second.Handle, 5); Pump(200);
                    var windows = Native.Enumerate(new Dictionary<IntPtr, long>(), true).Where(w => w.Handle == first.Handle || w.Handle == second.Handle).ToArray();
                    Check(windows.Length == 2, "preview test uses two real native windows");
                    app.OnWindows(windows);
                    var monitor = Native.TargetMonitor(false);
                    second.FormBorderStyle = FormBorderStyle.None; second.Bounds = monitor.Monitor.Bounds; second.TopMost = true;
                    Check(Native.SetWindowPos(second.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x40), "borderless monitor-sized topmost test window is raised");
                    Pump(200); // Drain activation messages from changing the fixture's native border style.
                    using (var alt = new AltSwitcher(windows, new SwitchCommand { Monitor = monitor, Foreground = first.Handle }, w => { }, app.Icons)) {
                        IntPtr previousFocus = Native.GetForegroundWindow();
                        alt.Present();
                        Check(alt.RaiseError == 0 && Above(alt.Handle, second.Handle), "Alt overlay appears above existing borderless topmost window in native Z order");
                        log.Add("Alt foreground immediately after Present=" + Native.GetForegroundWindow() + "; style=" + Native.GetWindowLongPtr(alt.Handle, -20).ToInt64().ToString("X"));
                        Pump(200);
                        IntPtr afterFocus = Native.GetForegroundWindow();
                        log.Add("Alt foreground before=" + previousFocus + "; after=" + afterFocus + "; overlay=" + alt.Handle);
                        Check(afterFocus != alt.Handle && (Native.GetWindowLongPtr(alt.Handle, -20).ToInt64() & 0x08000000) != 0, "Alt overlay retains no-activate style and does not acquire foreground");
                        if (afterFocus != previousFocus) log.Add("BLOCKED: foreground changed externally during the fixture; preserving the original game focus still needs manual confirmation.");
                        Check(alt.PreviewCount == 2 && LiveThumbnail.ActiveCount == 2, "Alt surface registers and updates two DWM live thumbnails");
                        first.Controls[0].Text = "CONTENT CHANGED\n\nDWM reflects source updates\nwithout a screenshot timer."; Pump(100);
                        alt.Step(false); Check(alt.PreviewCount == 2, "selection updates preserve native thumbnail registrations");
                        alt.Hide(); Check(LiveThumbnail.ActiveCount == 0, "hiding Alt overlay releases every DWM relationship");
                        alt.Show(); Pump(100); Check(alt.PreviewCount == 2, "reopening Alt overlay recreates thumbnails");
                    }
                    Check(LiveThumbnail.ActiveCount == 0, "disposing Alt overlay releases thumbnail handles");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = monitor }); Pump(100);
                    Check(app.ControlPanel == null && app.OverviewPanel.PreviewCount == 2, "Win+Tab shows DWM overview without constructing control panel");
                    log.Add("DWM acrylic requested=" + app.OverviewPanel.AcrylicEnabled + "; result=0x" + app.OverviewPanel.BackdropResult.ToString("X8"));
                    if (app.OverviewPanel.AcrylicEnabled && Native.GetForegroundWindow() == app.OverviewPanel.Handle) {
                        var panel = app.OverviewPanel; Point sample = new Point(panel.Left + 5, panel.Bottom - 45);
                        second.BackColor = Color.FromArgb(210, 45, 60); second.Refresh(); Pump(250);
                        Color red = SampleScreen(sample);
                        second.BackColor = Color.FromArgb(30, 90, 220); second.Refresh(); Pump(250);
                        Color blue = SampleScreen(sample);
                        if (Native.GetForegroundWindow() == panel.Handle && panel.Visible) {
                            Check(Math.Abs(red.R - blue.R) + Math.Abs(red.G - blue.G) + Math.Abs(red.B - blue.B) > 15,
                                "composited translucent background responds to a live source color change (" + red + " -> " + blue + ")");
                            using (var capture = new Bitmap(panel.Width, panel.Height)) {
                                using (var graphics = Graphics.FromImage(capture)) graphics.CopyFromScreen(panel.Location, Point.Empty, panel.Size);
                                capture.Save(Path.Combine(directory, "overview-live-composition.png"));
                            }
                        } else log.Add("BLOCKED: foreground changed during the acrylic sample; no visual transparency claim.");
                    } else log.Add("BLOCKED: acrylic disabled/unsupported or panel not foreground; visual backdrop requires manual check.");
                    Check(app.OverviewPanel.RaiseError == 0 && Above(app.OverviewPanel.Handle, second.Handle), "Win overview is above borderless topmost fixture");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Cancel });
                    Native.SetWindowPos(second.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x40);
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = monitor }); Pump(100);
                    Check(app.OverviewPanel.RaiseError == 0 && Above(app.OverviewPanel.Handle, second.Handle), "reused Win overview reasserts Z order on reopening");
                    var group = app.Model.AddGroup("Second group"); app.Model.Move(windows[1], group.Id); app.Changed();
                    Check(app.OverviewPanel.PreviewCount == 2, "regrouping keeps real thumbnails available");
                    app.SetOverviewMode(true); Pump(100);
                    Check(app.OverviewPanel.PreviewCount == 2 && app.OverviewPanel.GroupHeaderCount == 0, "changing live overview layout preserves both DWM thumbnails");
                    if (Native.GetForegroundWindow() == app.OverviewPanel.Handle && app.OverviewPanel.Visible)
                        using (var capture = new Bitmap(app.OverviewPanel.Width, app.OverviewPanel.Height)) {
                            using (var graphics = Graphics.FromImage(capture)) graphics.CopyFromScreen(app.OverviewPanel.Location, Point.Empty, app.OverviewPanel.Size);
                            capture.Save(Path.Combine(directory, "overview-compact-live.png"));
                        }
                    app.SetOverviewMode(false);
                    app.OverviewPanel.Size = new Size(800, 400); Pump(50);
                    Check(app.OverviewPanel.PreviewCount == 1 && LiveThumbnail.ActiveCount == 1, "offscreen group previews are unregistered in a small viewport");
                    app.OverviewPanel.ScrollContent(10000); Pump(50);
                    Check(app.OverviewPanel.PreviewCount == 1 && LiveThumbnail.ActiveCount == 1, "scrolling registers the new visible preview and releases the old one");
                    app.OverviewPanel.Bounds = monitor.Work.Bounds; app.OverviewPanel.ScrollContent(-10000); Pump(50);
                    app.Model.SetVisibility(windows[0], false, true); app.Changed();
                    Check(app.OverviewPanel.PreviewCount == 1 && LiveThumbnail.ActiveCount == 1, "hiding a window releases its thumbnail immediately");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Cancel });
                    Check(LiveThumbnail.ActiveCount == 0, "closing Win+Tab leaves no thumbnail composition active");
                    Check(!app.OverviewPanel.BackdropVisible, "closing Win+Tab hides its separate translucent backdrop");
                    using (var external = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--fixture") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) {
                        WindowInfo source = null; var wait = Stopwatch.StartNew();
                        while (source == null && wait.ElapsedMilliseconds < 2500) {
                            Pump(100); source = Native.Enumerate(new Dictionary<IntPtr, long>(), false).FirstOrDefault(w => w.ProcessId == (uint)external.Id);
                        }
                        Check(source != null, "external-process preview fixture is discoverable");
                        if (source != null) using (var externalAlt = new AltSwitcher(new[] { source }, new SwitchCommand { Monitor = monitor }, w => { }, app.Icons)) {
                            externalAlt.Show(); Pump(100);
                            Check(externalAlt.PreviewCount == 1, "DWM previews work across process boundaries for a normal test window (" + externalAlt.ThumbnailResults + ")");
                        }
                        while (!external.HasExited && wait.ElapsedMilliseconds < 7000) Pump(100);
                        if (!external.HasExited) external.Kill();
                    }
                    log.Add("DWM API registration/update/lifecycle verified. DrawToBitmap cannot capture compositor pixels; live visual content, protected windows and minimized previews still need interactive confirmation.");
                }
            } catch (Exception e) { failures++; log.Add(e.ToString()); }
            return Finish(Path.Combine(directory, "preview-test.txt"));
        }
        private static Color SampleScreen(Point point)
        {
            using (var bitmap = new Bitmap(1, 1)) {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(point, Point.Empty, new Size(1, 1));
                return bitmap.GetPixel(0, 0);
            }
        }
        public static int UiRun(string directory)
        {
            log.Clear(); failures = 0; Directory.CreateDirectory(directory);
            try {
                using (var app = new AppController(Path.Combine(directory, "ui-test-settings.json"), true)) {
                    var windows = new[] {
                        Window("a", "專案規格 — WindowTabManager", "C:\\Editor.exe", "\\\\.\\DISPLAY1"),
                        Window("b", "API 文件 — Microsoft Learn", "C:\\Browser.exe", "\\\\.\\DISPLAY2"),
                        Window("c", "產品設計 — 工作筆記", "C:\\Editor.exe", "\\\\.\\DISPLAY2")
                    };
                    app.OnWindows(windows); app.Model.SetVisibility(windows[1], true, true);
                    app.ShowControl(); Pump(400);
                    using (var bitmap = new Bitmap(app.ControlPanel.Width, app.ControlPanel.Height)) { app.ControlPanel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, app.ControlPanel.Size)); bitmap.Save(Path.Combine(directory, "control-panel.png")); }
                    Check(app.ControlPanel.Visible, "control panel renders with sample data; no live-keyboard hook enabled");
                    app.ControlPanel.Close(); Check(!app.ControlPanel.Visible && !app.ControlPanel.IsDisposed, "closing panel hides it without disposing background controller");
                    var monitor = Native.TargetMonitor(false);
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = monitor }); Pump(300);
                    using (var bitmap = new Bitmap(app.OverviewPanel.Width, app.OverviewPanel.Height)) { app.OverviewPanel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, app.OverviewPanel.Size)); bitmap.Save(Path.Combine(directory, "overview.png")); }
                    Check(app.OverviewPanel.Visible, "persistent grouped overview renders");
                    Check(app.OverviewPanel.FormBorderStyle == FormBorderStyle.None && !app.OverviewPanel.ShowInTaskbar && !app.ControlPanel.Visible, "Win+Tab routes to borderless preview overlay, not management panel");
                    Check(app.OverviewPanel.CardCount == 3, "overview exposes preview cards for visible windows");
                    app.Model.SetVisibility(windows[1], false, true); app.Changed();
                    Check(app.OverviewPanel.CardCount == 2, "Win hiding removes preview card immediately");
                    var previewGroup = app.Model.AddGroup("預覽群組"); app.Model.Move(windows[0], previewGroup.Id); app.Changed();
                    Check(app.OverviewPanel.CardCount == 2 && app.Model.GroupName(windows[0]) == "預覽群組", "group editing updates the preview workspace");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Cancel }); Check(!app.OverviewPanel.Visible, "cancel closes overview");
                    Check(LiveThumbnail.ActiveCount == 0, "hidden overlay retains no live-thumbnail registrations");
                    using (var alt = new AltSwitcher(windows, new SwitchCommand { Monitor = monitor, Foreground = windows[0].Handle }, w => { }, app.Icons)) {
                        alt.Show(); Pump(100); alt.Step(true); Check(alt.Index == 0, "Alt switcher cycles through frozen candidate list");
                        using (var bitmap = new Bitmap(alt.Width, alt.Height)) { alt.DrawToBitmap(bitmap, new Rectangle(Point.Empty, alt.Size)); bitmap.Save(Path.Combine(directory, "alt-switcher.png")); }
                    }
                    app.Model.SetVisibility(windows[1], false, false); app.SetOverviewMode(true);
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = monitor });
                    Check(app.OverviewPanel.CardCount == 3 && app.OverviewPanel.GroupHeaderCount == 0, "compact mode packs windows from different groups without group rows");
                    if (monitor.Monitor.Bounds.Width >= 1000) Check(app.OverviewPanel.CardRectangle(0).Top == app.OverviewPanel.CardRectangle(1).Top, "windows from different groups share a row in compact mode");
                    using (var bitmap = new Bitmap(app.OverviewPanel.Width, app.OverviewPanel.Height)) { app.OverviewPanel.DrawToBitmap(bitmap, app.OverviewPanel.ClientRectangle); bitmap.Save(Path.Combine(directory, "overview-compact.png")); }
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Cancel });
                    string chosen = null, closed = null;
                    using (var alt = new AltSwitcher(windows, new SwitchCommand { Monitor = monitor }, w => chosen = w.Key, app.Icons, w => closed = w.Key)) {
                        alt.Show(); var closeBox = alt.CloseRectangle(0);
                        alt.ClickAt(new Point(closeBox.Left + 14, closeBox.Top + 14), MouseButtons.Left);
                        Check(closed == windows[0].Key && chosen == null, "Alt close hit target requests close without selecting the window");
                        alt.UpdateCandidates(new[] { windows[0], windows[2] });
                        Check(alt.CardCount == 2 && !alt.Candidates.Any(w => w.Key == windows[1].Key), "closed Alt candidate is removed without adding new windows");
                        Check(alt.Region != null && !alt.Region.IsVisible(0, 0), "Alt native window region has rounded corners");
                    }
                    int menuOpened = 0; app.TrayMenu.Opened += delegate { menuOpened++; };
                    app.TrayMenu.Show(new Point(monitor.Work.Left + 60, monitor.Work.Top + 60));
                    Check(menuOpened == 1 && app.TrayMenu.Visible, "tray menu opens on the first request");
                    Check(app.TrayMenu.Renderer is DarkMenuRenderer, "tray menu uses dark renderer");
                    using (var bitmap = new Bitmap(app.TrayMenu.Width, app.TrayMenu.Height)) { app.TrayMenu.DrawToBitmap(bitmap, app.TrayMenu.ClientRectangle); bitmap.Save(Path.Combine(directory, "tray-menu.png")); }
                    app.TrayMenu.Close();
                    app.SetShortcutEnabled(true, false);
                    Check(!app.AltEnabled && app.WinEnabled, "controller pauses Alt while keeping Win enabled");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.BeginAlt, Monitor = monitor });
                    Check(app.AltPanel == null, "queued Alt opening is ignored after pausing Alt");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = monitor });
                    Check(app.OverviewPanel.Visible, "Win overview remains usable with Alt paused");
                    app.SetShortcutEnabled(false, false);
                    Check(!app.AltEnabled && !app.WinEnabled && !app.OverviewPanel.Visible, "pausing Win closes its open panel and leaves both shortcuts disabled");
                    app.SetShortcutEnabled(true, true);
                    Check(app.AltEnabled && !app.WinEnabled, "Alt can resume without resuming Win");
                    app.OnCommand(new SwitchCommand { Action = SwitchAction.Overview, Monitor = monitor });
                    Check(!app.OverviewPanel.Visible, "queued Win opening is ignored while Win paused");
                    app.SetShortcutEnabled(false, true); app.SetOverviewMode(false);
                    string beforeExit = File.ReadAllText(Path.Combine(directory, "ui-test-settings.json"));
                    app.Model.AddGroup("Unsaved at exit"); app.ExitWithoutSaving();
                    Check(app.Exiting && File.ReadAllText(Path.Combine(directory, "ui-test-settings.json")) == beforeExit, "exit with dirty groups returns immediately without changing saved settings");
                }
            } catch (Exception e) { failures++; log.Add(e.ToString()); }
            return Finish(Path.Combine(directory, "ui-test.txt"));
        }
    }
}
