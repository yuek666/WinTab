using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowTabManager
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--fixture") return Tests.FixtureRun();
            if (args.Length > 0 && args[0] == "--self-test") return Tests.Run(args.Length > 1 ? args[1] : "self-test.txt");
            if (args.Length > 0 && args[0] == "--native-test") return Tests.NativeRun(args.Length > 1 ? args[1] : "native-test.txt");
            if (args.Length > 0 && args[0] == "--ui-test") return Tests.UiRun(args.Length > 1 ? args[1] : "artifacts");
            if (args.Length > 0 && args[0] == "--preview-test") return Tests.PreviewRun(args.Length > 1 ? args[1] : "artifacts");
            if (args.Length > 0 && args[0] == "--monitor-test") return Tests.MonitorRun(args.Length > 1 ? args[1] : "artifacts");
            if (args.Length > 0 && args[0] == "--perf-test") return Tests.PerfRun(args.Length > 1 ? args[1] : "artifacts");
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowTabManager");
            if (args.Length == 2 && args[0] == "--data-dir") directory = Path.GetFullPath(args[1]);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).Replace("-", "");
            bool first;
            using (var mutex = new Mutex(true, "Local\\WindowTabManager-" + hash, out first)) {
                if (!first) { MessageBox.Show("WindowTabManager 已在執行，請從系統匣開啟。", "WindowTabManager"); return 0; }
                AppController app = null;
                try {
                    app = new AppController(Path.Combine(directory, "settings.json"), false);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { app.FatalError(e.Exception); };
                    Application.Run(app); return 0;
                } catch (Exception e) { MessageBox.Show("無法啟動：" + e.Message + "\n既有設定不會被覆寫。", "WindowTabManager", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { if (app != null) app.Dispose(); mutex.ReleaseMutex(); }
            }
        }
    }

    internal sealed class AppController : ApplicationContext
    {
        public readonly Workspace Model;
        public readonly IconCache Icons;
        public bool Exiting { get; private set; }
        public string Status = "啟動中";
        private readonly string settingsPath;
        private readonly Control dispatcher;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private WindowInventory inventory;
        private KeyboardHook keyboard;
        private ManagerForm control;
        private OverviewSwitcher overview;
        private readonly List<OverviewSwitcher> overviews = new List<OverviewSwitcher>();
        private AltSwitcher alt;
        private bool initialized, paused, disposed;
        private readonly bool testMode;
        private string lastSnapshot;
        private int panelGeneration;
        private string lastAltAttempt = "Alt+Tab：尚未收到", lastWinAttempt = "Win+Tab：尚未收到";
        internal ManagerForm ControlPanel { get { return control; } }
        internal OverviewSwitcher OverviewPanel { get { return overview; } }
        internal OverviewSwitcher[] OverviewPanels { get { return overviews.ToArray(); } }
        internal ContextMenuStrip TrayMenu { get { return menu; } }
        internal AltSwitcher AltPanel { get { return alt; } }
        public bool AltEnabled { get { return !paused && !Model.Settings.PauseAlt; } }
        public bool WinEnabled { get { return !paused && !Model.Settings.PauseWin; } }

        public AppController(string settingsPath, bool testMode)
        {
            this.settingsPath = settingsPath; this.testMode = testMode;
            Model = new Workspace(JsonStore.Load(settingsPath));
            dispatcher = new Control(); var hwnd = dispatcher.Handle;
            Icons = new IconCache(dispatcher);
            Icons.Changed += delegate { if (control != null) control.RefreshIcons(); foreach (var panel in overviews) panel.RefreshIcons(); if (alt != null) alt.RefreshIcons(Icons); };
            menu = new DarkMenu();
            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) { BuildMenu(); e.Cancel = false; };
            tray = new NotifyIcon { Icon = AppIcons.Tray, Text = "WindowTabManager — 啟動中", ContextMenuStrip = menu, Visible = !testMode };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowControl(); };
            BuildMenu(); // Populate before the very first opening; an empty menu defaults to Cancel=true.
            SystemEvents.DisplaySettingsChanged += OnDisplayChange;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            if (!testMode) inventory = new WindowInventory(w => Post(delegate { OnWindows(w); }), s => Post(delegate { Report(s); }));
            // Hooks are enabled only after an initial snapshot exists, avoiding an empty cold start.
        }
        private void Post(Action action)
        {
            if (Exiting || dispatcher.IsDisposed) return;
            try { dispatcher.BeginInvoke((Action)delegate { if (!Exiting) action(); }); } catch (InvalidOperationException) { }
        }
        internal void OnWindows(WindowInfo[] windows)
        {
            Model.Update(windows);
            if (!initialized) {
                initialized = true;
                int startup = Model.Settings.StartupSlot;
                if (startup >= 0 && Model.Settings.Slots[startup] != null) Model.Load(startup); else Model.AutoGroup();
                if (!testMode) Resume(); else Status = "介面測試（未攔截快捷鍵）";
            }
            string signature = String.Join("\n", windows.Select(w => w.Key + w.Title + w.Monitor + w.Minimized));
            if (alt != null) alt.UpdateCandidates(windows);
            if (lastSnapshot == signature) { foreach (var panel in overviews) panel.RefreshView(false); return; }
            lastSnapshot = signature; Icons.Prune(windows); Changed();
        }
        private void BuildMenu()
        {
            while (menu.Items.Count > 0) { var item = menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
            menu.Items.Add("開啟控制面板", null, delegate { ShowControl(); });
            var profiles = new ToolStripMenuItem("切換已儲存組態");
            for (int i = 0; i < 3; i++) {
                int slot = i; var saved = Model.Settings.Slots[i];
                var entry = new ToolStripMenuItem((i + 1) + " · " + (saved == null ? "尚未儲存" : saved.Name)); entry.Enabled = saved != null;
                entry.Checked = Model.LoadedSlot == slot;
                entry.Click += delegate { LoadSlot(slot, null); }; profiles.DropDownItems.Add(entry);
            }
            menu.Items.Add(profiles); menu.Items.Add(new ToolStripSeparator());
            var altToggle = new ToolStripMenuItem("接管 Alt + Tab") { Checked = AltEnabled };
            altToggle.Click += delegate { SetShortcutEnabled(true, !AltEnabled); }; menu.Items.Add(altToggle);
            var winToggle = new ToolStripMenuItem("接管 Win + Tab") { Checked = WinEnabled };
            winToggle.Click += delegate { SetShortcutEnabled(false, !WinEnabled); }; menu.Items.Add(winToggle);
            menu.Items.Add(new ToolStripSeparator());
            var layout = new ToolStripMenuItem("Win + Tab 排列方式");
            var grouped = new ToolStripMenuItem("分組排列") { Checked = !Model.Settings.CompactOverview };
            var compact = new ToolStripMenuItem("原生並排") { Checked = Model.Settings.CompactOverview };
            grouped.Click += delegate { SetOverviewMode(false); }; compact.Click += delegate { SetOverviewMode(true); };
            layout.DropDownItems.Add(grouped); layout.DropDownItems.Add(compact); menu.Items.Add(layout);
            layout.DropDownItems.Add(new ToolStripSeparator());
            var centered = new ToolStripMenuItem("視窗置中排列") { Checked = Model.Settings.CenterOverview };
            centered.Click += delegate { SetOverviewCentered(!Model.Settings.CenterOverview); }; layout.DropDownItems.Add(centered);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("快捷鍵診斷", null, delegate { ShowShortcutDiagnostics(); });
            menu.Items.Add("退出", null, delegate { ExitWithoutSaving(); });
            ((DarkMenu)menu).StyleItems();
        }
        public void ExitWithoutSaving() { ExitThread(); }
        public void ShowControl()
        {
            CloseAlt(); CloseOverview();
            if (control == null || control.IsDisposed) control = new ManagerForm(this, false);
            control.Show(); control.WindowState = FormWindowState.Normal; control.Activate(); control.RefreshView(true);
        }
        internal void OnCommand(SwitchCommand command)
        {
            if (paused || Exiting) return;
            switch (command.Action) {
                case SwitchAction.BeginAlt:
                    if (!AltEnabled) return;
                    panelGeneration = command.Generation;
                    CloseAlt(); HideOverviews();
                    var candidates = Model.Windows.Where(w => w.Monitor == command.Monitor.Device && !Model.Hidden(w, true)).ToArray();
                    alt = new AltSwitcher(candidates, command, FocusWindow, Icons, RequestCloseWindow); alt.Present(); RecordAttempt(command, alt); break;
                case SwitchAction.StepForward: if (AltEnabled && alt != null) alt.Step(false); break;
                case SwitchAction.StepBackward: if (AltEnabled && alt != null) alt.Step(true); break;
                case SwitchAction.CommitAlt:
                    if (AltEnabled && alt != null) { var panel = alt; panel.Commit(); CloseAlt(); } break;
                case SwitchAction.Cancel: CloseAlt(); CloseOverview(); break;
                case SwitchAction.Overview:
                    if (!WinEnabled) return;
                    panelGeneration = command.Generation;
                    CloseAlt();
                    if (control != null) control.Hide();
                    ShowOverviews(command.Monitor.Device);
                    if (overview != null) RecordAttempt(command, overview); break;
            }
        }
        private void ShowOverviews(string preferredDevice)
        {
            var screens = Screen.AllScreens;
            foreach (var panel in overviews.ToArray()) {
                if (panel.IsDisposed || !screens.Any(s => s.DeviceName == panel.MonitorDevice)) {
                    panel.Dispose(); overviews.Remove(panel);
                }
            }
            foreach (var screen in screens) {
                var panel = overviews.FirstOrDefault(p => p.MonitorDevice == screen.DeviceName);
                if (panel == null) { panel = new OverviewSwitcher(this, screen.DeviceName); overviews.Add(panel); }
                panel.Bounds = screen.Bounds;
            }
            overview = overviews.FirstOrDefault(p => p.MonitorDevice == preferredDevice) ?? overviews.FirstOrDefault();
            // Other monitors must not steal keyboard focus while the session opens.
            foreach (var panel in overviews.Where(p => p != overview)) { panel.Present(false); panel.RefreshView(true); }
            if (overview != null) { overview.Present(); overview.RefreshView(true); }
        }
        private void HideOverviews() { foreach (var panel in overviews) { panel.HideSessionMenu(); panel.Hide(); } }
        public void CloseOverview() { HideOverviews(); PanelClosed(); }
        internal void DisplayConfigurationChanged()
        {
            CloseAlt(); CloseOverview();
            foreach (var panel in overviews) panel.Dispose();
            overviews.Clear(); overview = null; RefreshInventory();
        }
        private void RecordAttempt(SwitchCommand command, PreviewSurface panel)
        {
            uint pid; Native.GetWindowThreadProcessId(command.Foreground, out pid);
            string attempt = (command.Action == SwitchAction.BeginAlt ? "Alt+Tab" : "Win+Tab") + "：" + DateTime.Now.ToString("HH:mm:ss") +
                " 已收到\r\n來源 PID=" + pid + "（" + Native.Elevation(pid) + "）" +
                "；面板 Visible=" + panel.Visible + "；置頂錯誤碼=" + panel.RaiseError + "；候選=" + panel.CardCount;
            if (command.Action == SwitchAction.BeginAlt) lastAltAttempt = attempt;
            else lastWinAttempt = attempt + "\r\n各螢幕：" + String.Join("；", overviews.Select(p => p.MonitorDevice + " 候選=" + p.CardCount + " Visible=" + p.Visible + " 置頂錯誤碼=" + p.RaiseError));
        }
        private void ShowShortcutDiagnostics()
        {
            CloseAlt(); CloseOverview();
            using (var dialog = new Form { Text = "快捷鍵診斷", Size = new Size(690, 370), StartPosition = FormStartPosition.CenterScreen }) {
                Theme.Apply(dialog);
                dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
                    Text = "WindowTabManager 置中排列修訂 4\r\n本程式：" + Native.Elevation((uint)System.Diagnostics.Process.GetCurrentProcess().Id) +
                    "；" + Status + "\r\n\r\n" + lastAltAttempt + "\r\n\r\n" + lastWinAttempt +
                    "\r\n\r\n以上是最近一次開啟請求，並非目前可見狀態。可 Ctrl+A、Ctrl+C 複製。\r\n" +
                    "在遊戲裡按鍵後，時間未更新：請先暫停／恢復接管再測試。\r\n" +
                    "時間有更新但看不到：可能是遊戲顯示／置頂行為；置頂成功不保證畫面未被遮擋。\r\n" +
                    "來源為系統管理員、本程式為一般權限時，可退出後用相同權限重測。" });
                dialog.ShowDialog();
            }
        }
        public void FocusWindow(WindowInfo w)
        {
            CloseAlt(); CloseOverview(); if (control != null) control.Hide();
            if (!Native.Activate(w)) Report("無法切換：視窗已關閉，或 Windows 拒絕前景切換。請從工作列開啟。");
            RefreshInventory();
        }
        public void RequestCloseWindow(WindowInfo w)
        {
            CloseAlt(); CloseOverview();
            // Dismiss the switcher before the app can ask to save a document.
            Native.Activate(w);
            if (!Native.RequestClose(w)) Report("無法送出關閉要求：視窗已關閉或權限不足。");
            RefreshInventory();
        }
        private void CloseAlt() { if (alt != null) { var panel = alt; alt = null; panel.Close(); panel.Dispose(); } }
        public void PanelClosed() { if (keyboard != null) keyboard.ClosePanel(panelGeneration); }
        public string Prompt(IWin32Window owner, string title, string text)
        {
            // Let a modal dialog own Escape/Tab; the overview's ordinary Form keys resume afterward.
            PanelClosed(); return Theme.Prompt(owner, title, text);
        }
        public void Changed()
        {
            if (control != null) control.RefreshView(false);
            foreach (var panel in overviews) panel.RefreshView(false);
        }
        private bool Persist(Settings settings)
        {
            try { JsonStore.Save(settingsPath, settings); return true; }
            catch (Exception e) { MessageBox.Show("設定未能寫入磁碟：" + e.Message + "\n目前修改僅在本次執行有效。", "儲存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
        public void SetMonitorMode(bool active)
        {
            Model.Settings.UseActiveMonitor = active; if (keyboard != null) keyboard.UseActiveMonitor = active;
            Persist(Model.Settings); Changed();
        }
        public void SetOverviewMode(bool compact)
        {
            Model.Settings.CompactOverview = compact; Persist(Model.Settings); Changed();
        }
        public void SetOverviewCentered(bool centered)
        {
            Model.Settings.CenterOverview = centered; Persist(Model.Settings); Changed();
        }
        public void SetShortcutEnabled(bool altShortcut, bool enabled)
        {
            if (altShortcut) {
                Model.Settings.PauseAlt = !enabled;
                if (!enabled && alt != null) { CloseAlt(); PanelClosed(); }
            } else {
                Model.Settings.PauseWin = !enabled;
                if (!enabled && overviews.Any(p => p.Visible)) CloseOverview();
            }
            Persist(Model.Settings);
            if (Model.Settings.PauseAlt && Model.Settings.PauseWin) Pause();
            else if (keyboard == null && !testMode) Resume();
            else {
                paused = false;
                if (keyboard != null) keyboard.Configure(!Model.Settings.PauseAlt, !Model.Settings.PauseWin);
                UpdateShortcutStatus();
            }
            Changed();
        }
        private void UpdateShortcutStatus()
        {
            Status = "Alt+Tab " + (AltEnabled ? "接管中" : "已暫停") + " · Win+Tab " + (WinEnabled ? "接管中" : "已暫停");
            tray.Text = "WindowTabManager — " + Status;
        }
        public void SetHidden(WindowInfo w, bool altHidden, bool hidden) { Model.SetVisibility(w, altHidden, hidden); Persist(Model.Settings); Changed(); }
        public void SetStartup(int slot)
        {
            if (slot >= 0 && Model.Settings.Slots[slot] == null) { MessageBox.Show("請先儲存該欄位，再指定為啟動預設。", "啟動預設"); return; }
            int old = Model.Settings.StartupSlot; Model.Settings.StartupSlot = slot;
            if (!Persist(Model.Settings)) Model.Settings.StartupSlot = old;
            Changed();
        }
        public bool SaveSlot(int slot, IWin32Window owner)
        {
            if (slot < 0 || slot > 2) return false;
            var previous = Model.Settings.Slots[slot];
            string name = Prompt(owner, "儲存至欄位 " + (slot + 1) + (previous == null ? "" : "（更新既有組態）"), previous == null ? "組態 " + (slot + 1) : previous.Name);
            if (name == null) return false;
            var snapshot = Model.Capture(name);
            var transaction = JsonStore.Clone(Model.Settings); transaction.Slots[slot] = snapshot;
            if (!Persist(transaction)) return false;
            Model.AcceptSaved(slot, snapshot); Changed(); return true;
        }
        public void LoadSlot(int slot, IWin32Window owner)
        {
            if (slot < 0 || slot > 2 || !ResolveDirty(owner)) return;
            Model.Load(slot); Changed();
        }
        public bool ResolveDirty(IWin32Window owner)
        {
            if (!Model.Dirty) return true;
            CloseAlt(); PanelClosed();
            if (owner == null) {
                owner = overviews.FirstOrDefault(p => p.Visible && p.ContainsFocus) ?? overviews.FirstOrDefault(p => p.Visible);
                if (owner == null && control != null && control.Visible) owner = control;
            }
            using (var dialog = new Form { Text = "分組有尚未儲存的修改", Size = new Size(520, 205), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false }) {
                Theme.Apply(dialog); dialog.Padding = new Padding(14);
                var label = new Label { Dock = DockStyle.Top, Height = 48, Text = "是否先儲存目前分組？\n捨棄只會放棄本次分組修改，不會修改已儲存組態。" };
                var slots = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
                for (int i = 0; i < 3; i++) slots.Items.Add("欄位 " + (i + 1) + " · " + (Model.Settings.Slots[i] == null ? "空白" : Model.Settings.Slots[i].Name));
                slots.SelectedIndex = Math.Max(0, Model.LoadedSlot);
                var row = Theme.Bar(); row.Dock = DockStyle.Bottom;
                row.Controls.Add(Theme.Button("儲存至選定欄位", delegate { if (SaveSlot(slots.SelectedIndex, dialog)) dialog.DialogResult = DialogResult.Yes; }));
                row.Controls.Add(Theme.Button("捨棄並繼續", delegate { dialog.DialogResult = DialogResult.No; }));
                var cancel = Theme.Button("取消", delegate { dialog.DialogResult = DialogResult.Cancel; }); row.Controls.Add(cancel); dialog.CancelButton = cancel;
                dialog.Controls.Add(slots); dialog.Controls.Add(label); dialog.Controls.Add(row);
                var result = dialog.ShowDialog(owner);
                return result == DialogResult.Yes || result == DialogResult.No;
            }
        }
        public void TogglePause()
        {
            bool enable = !AltEnabled && !WinEnabled;
            SetShortcutEnabled(true, enable); SetShortcutEnabled(false, enable);
        }
        private void Pause()
        {
            paused = true; if (keyboard != null) { keyboard.Dispose(); keyboard = null; }
            Status = "已暫停 · 原生快捷鍵可用"; tray.Text = "WindowTabManager — 已暫停";
        }
        private void Resume()
        {
            if (Model.Settings.PauseAlt && Model.Settings.PauseWin) { Pause(); return; }
            try {
                keyboard = new KeyboardHook(c => Post(delegate { OnCommand(c); }), s => Post(delegate { Report(s); }), !Model.Settings.PauseAlt, !Model.Settings.PauseWin);
                keyboard.UseActiveMonitor = Model.Settings.UseActiveMonitor; paused = false;
                UpdateShortcutStatus();
            } catch (Exception e) { paused = true; Status = "攔截未啟用"; Report(e.Message); }
        }
        private void Report(string message)
        {
            Status = message; Changed();
            if (!testMode) { tray.BalloonTipTitle = "WindowTabManager"; tray.BalloonTipText = message; tray.ShowBalloonTip(4000); }
        }
        public void RefreshInventory() { if (inventory != null) inventory.RequestRefresh(); }
        private void OnDisplayChange(object sender, EventArgs e)
        {
            Post(DisplayConfigurationChanged);
        }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
                Post(delegate { if (keyboard != null) keyboard.ResyncModifiers(); });
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
                Post(delegate { CloseAlt(); CloseOverview(); });
        }
        public void FatalError(Exception e)
        {
            Pause(); CloseAlt(); CloseOverview();
            MessageBox.Show("已停用快捷鍵接管：" + e.Message + "\n可從系統匣恢復，或退出後重新啟動。", "WindowTabManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        protected override void ExitThreadCore() { Exiting = true; Dispose(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) {
                disposed = true; Exiting = true;
                SystemEvents.DisplaySettingsChanged -= OnDisplayChange; SystemEvents.SessionSwitch -= OnSessionSwitch;
                if (keyboard != null) keyboard.Dispose(); if (inventory != null) inventory.Dispose();
                CloseAlt(); if (control != null) control.Dispose(); foreach (var panel in overviews) panel.Dispose(); overviews.Clear(); overview = null;
                tray.Visible = false; tray.Dispose(); menu.Dispose(); Icons.Dispose(); dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
