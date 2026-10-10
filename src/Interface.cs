using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowTabManager
{
    internal static class Theme
    {
        internal static readonly Color Ink = Color.FromArgb(25, 38, 54), Blue = Color.FromArgb(26, 91, 157), Paper = Color.FromArgb(245, 247, 250);
        internal static void Apply(Form form)
        {
            form.Font = new Font("Microsoft JhengHei UI", 10F);
            form.BackColor = Paper; form.ForeColor = Ink; form.Icon = AppIcons.Window;
            form.AutoScaleMode = AutoScaleMode.Dpi;
        }
        internal static Button Button(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3), FlatStyle = FlatStyle.System };
            b.Click += click; return b;
        }
        internal static FlowLayoutPanel Bar() { return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 6, 10, 6), WrapContents = true }; }
        internal static Label Label(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(4, 9, 4, 3) }; }
        internal static string Prompt(IWin32Window owner, string title, string text)
        {
            using (var dialog = new Form { Text = title, Size = new Size(430, 165), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false }) {
                Apply(dialog);
                var input = new TextBox { Text = text, Dock = DockStyle.Top, Margin = new Padding(16) };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
                var ok = Button("確定", delegate { if (!String.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = DialogResult.OK; });
                var cancel = Button("取消", delegate { dialog.DialogResult = DialogResult.Cancel; });
                buttons.Controls.Add(ok); buttons.Controls.Add(cancel); dialog.Controls.Add(input); dialog.Controls.Add(buttons);
                dialog.Padding = new Padding(16); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }
    }

    // Icon acquisition is bounded, asynchronous, and only requested by visible panels.
    internal sealed class IconCache : IDisposable
    {
        private readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        private readonly HashSet<string> pending = new HashSet<string>();
        private readonly Control dispatcher;
        private bool disposed;
        public event Action Changed;
        public IconCache(Control dispatcher) { this.dispatcher = dispatcher; }
        public Bitmap Get(WindowInfo w)
        {
            Bitmap bitmap;
            if (cache.TryGetValue(w.Key, out bitmap)) return bitmap;
            if (pending.Count < 4 && pending.Add(w.Key)) {
                Task.Run(delegate {
                    Bitmap result = null;
                    try { using (var icon = Native.ReadIcon(w.Handle)) result = icon.ToBitmap(); } catch { result = SystemIcons.Application.ToBitmap(); }
                    if (disposed) { if (result != null) result.Dispose(); return; }
                    try {
                        dispatcher.BeginInvoke((Action)delegate {
                            pending.Remove(w.Key);
                            if (disposed) { if (result != null) result.Dispose(); return; }
                            if (result != null) cache[w.Key] = result;
                            if (Changed != null) Changed();
                        });
                    } catch (InvalidOperationException) { if (result != null) result.Dispose(); }
                });
            }
            return null;
        }
        public void Prune(IEnumerable<WindowInfo> windows)
        {
            var keys = new HashSet<string>(windows.Select(w => w.Key));
            foreach (var key in cache.Keys.Where(k => !keys.Contains(k)).ToArray()) { cache[key].Dispose(); cache.Remove(key); }
        }
        public void Dispose() { disposed = true; foreach (var b in cache.Values) b.Dispose(); cache.Clear(); }
    }

    internal sealed class ManagerForm : Form
    {
        private readonly AppController app;
        private readonly bool overview;
        private readonly ListView list;
        private readonly ImageList icons = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
        private readonly ComboBox groupBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly ComboBox slotBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 215 };
        private readonly Label stateLabel;
        private readonly CheckBox activeMonitor = new CheckBox { AutoSize = true, Text = "Alt+Tab 依作用中視窗判定螢幕", Margin = new Padding(8) };
        private bool updating;
        private string lastSignature;
        public ManagerForm(AppController app, bool overview)
        {
            this.app = app; this.overview = overview;
            Theme.Apply(this); Text = overview ? "WindowTabManager — 群組總覽" : "WindowTabManager — 控制面板";
            Size = new Size(1100, 680); MinimumSize = new Size(820, 480); StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = !overview; TopMost = overview; KeyPreview = true;
            var header = new Label { Dock = DockStyle.Top, Height = 62, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Padding = new Padding(16, 16, 0, 0), Text = overview ? "群組總覽" : "視窗管理", ForeColor = Theme.Blue };
            stateLabel = new Label { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(14, 8, 10, 0), AutoEllipsis = true };
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, SmallImageList = icons, ShowGroups = true, BorderStyle = BorderStyle.None };
            list.Columns.Add("視窗標題（點選切換）", 365); list.Columns.Add("應用程式", 150); list.Columns.Add("螢幕", 130);
            list.Columns.Add("群組", 125); list.Columns.Add("Alt+Tab", 90); list.Columns.Add("Win+Tab", 95); list.Columns.Add("狀態", 80);
            list.MouseClick += OnListClick;
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && Selected != null) { app.FocusWindow(Selected); e.Handled = true; } };
            list.SelectedIndexChanged += delegate { UpdateStatus(); };
            var context = new ContextMenuStrip();
            context.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                context.Items.Clear(); var w = Selected;
                if (w == null) { e.Cancel = true; return; }
                context.Items.Add("切換至此視窗", null, delegate { app.FocusWindow(w); });
                var move = new ToolStripMenuItem("移至群組");
                move.DropDownItems.Add("未分組", null, delegate { app.Model.Move(w, null); app.Changed(); });
                foreach (var g in app.Model.Current.Groups) { var group = g; move.DropDownItems.Add(group.Name, null, delegate { app.Model.Move(w, group.Id); app.Changed(); }); }
                context.Items.Add(move);
                context.Items.Add(app.Model.Hidden(w, true) ? "在 Alt+Tab 顯示" : "從 Alt+Tab 隱藏", null, delegate { app.SetHidden(w, true, !app.Model.Hidden(w, true)); });
                context.Items.Add(app.Model.Hidden(w, false) ? "在 Win+Tab 顯示" : "從 Win+Tab 隱藏", null, delegate { app.SetHidden(w, false, !app.Model.Hidden(w, false)); });
            };
            list.ContextMenuStrip = context;
            var groups = Theme.Bar();
            groups.Controls.Add(Theme.Label("群組")); groups.Controls.Add(groupBox);
            groups.Controls.Add(Theme.Button("移入選定群組", delegate { if (Selected != null) { app.Model.Move(Selected, ((GroupInfo)groupBox.SelectedItem).Id); app.Changed(); } }));
            groups.Controls.Add(Theme.Button("新增", delegate { var name = app.Prompt(this, "新增群組", "新群組"); if (name != null) { app.Model.AddGroup(name); app.Changed(); } }));
            groups.Controls.Add(Theme.Button("重新命名", delegate {
                var g = groupBox.SelectedItem as GroupInfo; if (g == null || g.Id == null) return;
                var name = app.Prompt(this, "重新命名群組", g.Name); if (name != null) { g.Name = name; app.Model.Dirty = true; app.Changed(); }
            }));
            groups.Controls.Add(Theme.Button("刪除", delegate {
                var g = groupBox.SelectedItem as GroupInfo; if (g == null || g.Id == null) return;
                app.Model.DeleteGroup(g); app.Changed();
            }));
            groups.Controls.Add(Theme.Button("套用應用程式分組", delegate { if (app.ResolveDirty(this)) { app.Model.AutoGroup(); app.Changed(); } }));
            var profiles = Theme.Bar();
            if (!overview) {
                profiles.Controls.Add(Theme.Label("儲存欄位")); profiles.Controls.Add(slotBox);
                profiles.Controls.Add(Theme.Button("載入", delegate { app.LoadSlot(slotBox.SelectedIndex, this); }));
                profiles.Controls.Add(Theme.Button("命名並儲存／更新", delegate { app.SaveSlot(slotBox.SelectedIndex, this); }));
                profiles.Controls.Add(Theme.Button("設為啟動預設", delegate { app.SetStartup(slotBox.SelectedIndex); }));
                profiles.Controls.Add(Theme.Button("清除啟動預設", delegate { app.SetStartup(-1); }));
            } else {
                profiles.Controls.Add(Theme.Label("點標題直達；點其他欄位選取，再用上方工具或右鍵編輯。Esc 關閉。"));
                profiles.Controls.Add(Theme.Button("控制面板／組態", delegate { app.ShowControl(); }));
            }
            var options = Theme.Bar();
            if (!overview) {
                activeMonitor.Checked = app.Model.Settings.UseActiveMonitor;
                activeMonitor.CheckedChanged += delegate { if (!updating) app.SetMonitorMode(activeMonitor.Checked); };
                options.Controls.Add(activeMonitor);
                options.Controls.Add(Theme.Button("切換 Alt 隱藏", delegate { if (Selected != null) app.SetHidden(Selected, true, !app.Model.Hidden(Selected, true)); }));
                options.Controls.Add(Theme.Button("切換 Win 隱藏", delegate { if (Selected != null) app.SetHidden(Selected, false, !app.Model.Hidden(Selected, false)); }));
                options.Controls.Add(Theme.Button("重新整理", delegate { app.RefreshInventory(); }));
                options.Controls.Add(Theme.Button("暫停／恢復", delegate { app.TogglePause(); }));
            }
            Controls.Add(list); Controls.Add(options); Controls.Add(groups); Controls.Add(profiles); Controls.Add(header); Controls.Add(stateLabel);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!app.Exiting) { e.Cancel = true; Hide(); if (overview) app.PanelClosed(); } };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Hide(); if (overview) app.PanelClosed(); e.Handled = true; } };
            VisibleChanged += delegate { if (Visible) RefreshView(true); };
            FormClosed += delegate { icons.Dispose(); context.Dispose(); };
        }
        private WindowInfo Selected { get { return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as WindowInfo; } }
        private void OnListClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var hit = list.HitTest(e.Location);
            if (hit.Item != null && hit.SubItem == hit.Item.SubItems[0]) app.FocusWindow((WindowInfo)hit.Item.Tag);
        }
        private void UpdateStatus()
        {
            var w = Selected;
            stateLabel.Text = app.Status + "  ·  " + app.Model.Windows.Length + " 個視窗  ·  " + (app.Model.Dirty ? "分組尚未儲存" : "分組已載入／儲存") +
                (w == null ? "\n點標題切換；點其他欄位選取。隱藏設定立即保存，組態須手動儲存。" : "\n選取：" + w.Title + (String.IsNullOrEmpty(w.Executable) ? "（無法讀取路徑，僅本次有效）" : ""));
        }
        public void RefreshView(bool force)
        {
            if (!Visible && !force) return;
            var model = app.Model;
            string signature = model.Current.GetHashCode() + String.Join("\n", model.Windows.Select(w => w.Key + w.Title + w.Monitor + w.Minimized + model.GroupOf(w) + model.Hidden(w, true) + model.Hidden(w, false))) +
                String.Join("|", model.Current.Groups.Select(g => g.Id + g.Name));
            UpdateStatus();
            updating = true;
            activeMonitor.Checked = model.Settings.UseActiveMonitor;
            int slot = Math.Max(0, slotBox.SelectedIndex); slotBox.Items.Clear();
            for (int i = 0; i < 3; i++) slotBox.Items.Add((i + 1) + " · " + (model.Settings.Slots[i] == null ? "空白" : model.Settings.Slots[i].Name) + (model.Settings.StartupSlot == i ? " ★" : ""));
            slotBox.SelectedIndex = slot;
            updating = false;
            if (!force && lastSignature == signature) return;
            lastSignature = signature;
            string selected = Selected == null ? null : Selected.Key;
            string top = list.TopItem == null ? null : ((WindowInfo)list.TopItem.Tag).Key;
            string groupId = groupBox.SelectedItem == null ? null : ((GroupInfo)groupBox.SelectedItem).Id;
            groupBox.Items.Clear(); groupBox.Items.Add(new GroupInfo { Name = "未分組" });
            foreach (var g in model.Current.Groups) groupBox.Items.Add(g);
            groupBox.SelectedIndex = 0;
            for (int i = 0; i < groupBox.Items.Count; i++) if (((GroupInfo)groupBox.Items[i]).Id == groupId) groupBox.SelectedIndex = i;
            list.BeginUpdate(); list.Items.Clear(); list.Groups.Clear(); icons.Images.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var g in model.Current.Groups.Concat(new[] { new GroupInfo { Id = "", Name = "未分組" } })) {
                var lg = new ListViewGroup(g.Name, HorizontalAlignment.Left); groups[g.Id] = lg; list.Groups.Add(lg);
            }
            foreach (var w in model.Windows.Where(w => !overview || !model.Hidden(w, false))) {
                var item = new ListViewItem(w.Title) { Tag = w, Group = groups[model.GroupOf(w) ?? ""] };
                item.SubItems.Add(w.AppName); item.SubItems.Add(w.Monitor.Replace("\\\\.\\", "")); item.SubItems.Add(model.GroupName(w));
                item.SubItems.Add(model.Hidden(w, true) ? "隱藏" : "顯示"); item.SubItems.Add(model.Hidden(w, false) ? "隱藏" : "顯示"); item.SubItems.Add(w.Minimized ? "最小化" : "開啟");
                var image = app.Icons.Get(w);
                if (image != null) { icons.Images.Add(w.Key, image); item.ImageKey = w.Key; }
                list.Items.Add(item); if (w.Key == selected) item.Selected = true;
            }
            list.EndUpdate();
            if (top != null) foreach (ListViewItem item in list.Items) if (((WindowInfo)item.Tag).Key == top) { list.TopItem = item; break; }
        }
        public void RefreshIcons()
        {
            if (!Visible) return;
            foreach (ListViewItem item in list.Items) {
                var w = (WindowInfo)item.Tag;
                if (icons.Images.ContainsKey(w.Key)) continue;
                var b = app.Icons.Get(w);
                if (b != null) { icons.Images.Add(w.Key, b); item.ImageKey = w.Key; }
            }
        }
    }

}
