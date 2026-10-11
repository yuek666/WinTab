using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WindowTabManager
{
    internal sealed class ManagerForm : Form
    {
        private readonly AppController app;
        private readonly ListView list;
        private readonly ImageList icons = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
        private readonly ComboBox groupBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly ComboBox slotBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 215 };
        private readonly Label stateLabel;
        private readonly CheckBox activeMonitor = new CheckBox { AutoSize = true, Text = "Alt+Tab 依作用中視窗判定螢幕", Margin = new Padding(8) };
        private readonly CheckBox altEnabled = new CheckBox { AutoSize = true, Text = "接管 Alt + Tab", Margin = new Padding(8) };
        private readonly CheckBox winEnabled = new CheckBox { AutoSize = true, Text = "接管 Win + Tab", Margin = new Padding(8) };
        private readonly ComboBox layoutBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132 };
        private readonly ContextMenuStrip context;
        private bool updating;
        private string lastSignature;
        public ManagerForm(AppController app, bool unusedOverview)
        {
            this.app = app; Theme.Apply(this); Text = "WindowTabManager — 控制面板";
            Size = new Size(1180, 780); MinimumSize = new Size(1020, 720); StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true; Padding = new Padding(20, 0, 20, 10);
            var header = new Label { Dock = DockStyle.Top, Height = 72, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Padding = new Padding(2, 20, 0, 0), Text = "視窗管理", ForeColor = Theme.Ink };
            stateLabel = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(2, 10, 0, 0), AutoEllipsis = true, ForeColor = Color.FromArgb(101, 112, 128) };

            var settings = new TableLayoutPanel { Dock = DockStyle.Top, Height = 184, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 12) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            var profiles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            profiles.Controls.Add(slotBox);
            profiles.Controls.Add(Theme.Button("載入", delegate { app.LoadSlot(slotBox.SelectedIndex, this); }));
            var save = Theme.Button("命名並儲存", delegate { app.SaveSlot(slotBox.SelectedIndex, this); });
            save.BackColor = Color.FromArgb(38, 139, 255); save.ForeColor = Color.White; save.FlatAppearance.BorderSize = 0; save.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 111, 215);
            profiles.Controls.Add(save); profiles.SetFlowBreak(save, true);
            profiles.Controls.Add(Theme.Button("設為啟動預設", delegate { app.SetStartup(slotBox.SelectedIndex); }));
            profiles.Controls.Add(Theme.Button("清除啟動預設", delegate { app.SetStartup(-1); }));
            settings.Controls.Add(Card("分組組態", profiles), 0, 0);

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 0, 0, 0) };
            options.Controls.Add(altEnabled); options.Controls.Add(winEnabled); options.SetFlowBreak(winEnabled, true);
            options.Controls.Add(activeMonitor); options.SetFlowBreak(activeMonitor, true);
            options.Controls.Add(Theme.Label("Win+Tab 排列")); layoutBox.Items.AddRange(new object[] { "分組排列", "原生並排" }); options.Controls.Add(layoutBox);
            settings.Controls.Add(Card("快捷鍵與切換", options), 1, 0);
            altEnabled.CheckedChanged += delegate { if (!updating) app.SetShortcutEnabled(true, altEnabled.Checked); };
            winEnabled.CheckedChanged += delegate { if (!updating) app.SetShortcutEnabled(false, winEnabled.Checked); };
            activeMonitor.CheckedChanged += delegate { if (!updating) app.SetMonitorMode(activeMonitor.Checked); };
            layoutBox.SelectedIndexChanged += delegate { if (!updating) app.SetOverviewMode(layoutBox.SelectedIndex == 1); };

            var groups = Theme.Bar(); groups.BackColor = Color.White; groups.Padding = new Padding(8, 8, 8, 8);
            groups.Controls.Add(Theme.Label("群組")); groups.Controls.Add(groupBox);
            groups.Controls.Add(Theme.Button("移入群組", delegate { if (Selected != null && groupBox.SelectedItem != null) { app.Model.Move(Selected, ((GroupInfo)groupBox.SelectedItem).Id); app.Changed(); } }));
            groups.Controls.Add(Theme.Button("新增", delegate { var name = app.Prompt(this, "新增群組", "新群組"); if (name != null) { app.Model.AddGroup(name); app.Changed(); } }));
            groups.Controls.Add(Theme.Button("重新命名", delegate {
                var g = groupBox.SelectedItem as GroupInfo; if (g == null || g.Id == null) return;
                var name = app.Prompt(this, "重新命名群組", g.Name); if (name != null) { g.Name = name; app.Model.Dirty = true; app.Changed(); }
            }));
            groups.Controls.Add(Theme.Button("刪除群組", delegate { var g = groupBox.SelectedItem as GroupInfo; if (g == null || g.Id == null) return; app.Model.DeleteGroup(g); app.Changed(); }));
            groups.Controls.Add(Theme.Button("依應用程式分組", delegate { if (app.ResolveDirty(this)) { app.Model.AutoGroup(); app.Changed(); } }));

            var toolbar = Theme.Bar(); toolbar.Padding = new Padding(4, 9, 4, 9);
            toolbar.Controls.Add(Theme.Label("視窗清單"));
            toolbar.Controls.Add(Theme.Button("切換 Alt 隱藏", delegate { if (Selected != null) app.SetHidden(Selected, true, !app.Model.Hidden(Selected, true)); }));
            toolbar.Controls.Add(Theme.Button("切換 Win 隱藏", delegate { if (Selected != null) app.SetHidden(Selected, false, !app.Model.Hidden(Selected, false)); }));
            toolbar.Controls.Add(Theme.Button("重新整理", delegate { app.RefreshInventory(); }));
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, SmallImageList = icons, ShowGroups = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Ink };
            list.Columns.Add("視窗標題", 340); list.Columns.Add("應用程式", 155); list.Columns.Add("螢幕", 120);
            list.Columns.Add("群組", 140); list.Columns.Add("Alt+Tab", 95); list.Columns.Add("Win+Tab", 95); list.Columns.Add("狀態", 85);
            list.OwnerDraw = true;
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e) {
                using (var b = new SolidBrush(Color.FromArgb(240, 244, 249))) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, Rectangle.Inflate(e.Bounds, -8, 0), Color.FromArgb(85, 99, 119), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            list.MouseClick += OnListClick;
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && Selected != null) { app.FocusWindow(Selected); e.Handled = true; } };
            list.SelectedIndexChanged += delegate { UpdateStatus(); };
            context = new DarkMenu();
            context.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                while (context.Items.Count > 0) { var item = context.Items[0]; context.Items.RemoveAt(0); item.Dispose(); }
                var w = Selected;
                if (w == null) { e.Cancel = true; return; }
                context.Items.Add("切換至此視窗", null, delegate { app.FocusWindow(w); });
                var move = new ToolStripMenuItem("移至群組");
                move.DropDownItems.Add("未分組", null, delegate { app.Model.Move(w, null); app.Changed(); });
                foreach (var g in app.Model.Current.Groups) { var group = g; move.DropDownItems.Add(group.Name, null, delegate { app.Model.Move(w, group.Id); app.Changed(); }); }
                context.Items.Add(move);
                context.Items.Add(app.Model.Hidden(w, true) ? "在 Alt+Tab 顯示" : "從 Alt+Tab 隱藏", null, delegate { app.SetHidden(w, true, !app.Model.Hidden(w, true)); });
                context.Items.Add(app.Model.Hidden(w, false) ? "在 Win+Tab 顯示" : "從 Win+Tab 隱藏", null, delegate { app.SetHidden(w, false, !app.Model.Hidden(w, false)); });
                e.Cancel = false;
            };
            list.ContextMenuStrip = context;
            Controls.Add(list); Controls.Add(toolbar); Controls.Add(groups); Controls.Add(settings); Controls.Add(header); Controls.Add(stateLabel);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!app.Exiting) { e.Cancel = true; Hide(); } };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; } };
            VisibleChanged += delegate { if (Visible) RefreshView(true); };
        }
        private Panel Card(string title, Control body)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14), Margin = new Padding(0, 0, 12, 0) };
            var label = new Label { Dock = DockStyle.Top, Height = 32, Text = title, Font = new Font(Font, FontStyle.Bold), ForeColor = Theme.Blue };
            card.Controls.Add(body); card.Controls.Add(label);
            card.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(221, 228, 237))) e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
            return card;
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
            stateLabel.Text = app.Status + "    ·    " + app.Model.Windows.Length + " 個視窗    ·    " + (app.Model.Dirty ? "分組尚未儲存" : "分組已儲存／載入");
        }
        public void RefreshView(bool force)
        {
            if (!Visible && !force) return;
            var model = app.Model;
            string signature = model.Current.GetHashCode() + String.Join("\n", model.Windows.Select(w => w.Key + w.Title + w.Monitor + w.Minimized + model.GroupOf(w) + model.Hidden(w, true) + model.Hidden(w, false))) + String.Join("|", model.Current.Groups.Select(g => g.Id + g.Name));
            UpdateStatus(); updating = true;
            activeMonitor.Checked = model.Settings.UseActiveMonitor; altEnabled.Checked = app.AltEnabled; winEnabled.Checked = app.WinEnabled; layoutBox.SelectedIndex = model.Settings.CompactOverview ? 1 : 0;
            int slot = Math.Max(0, slotBox.SelectedIndex); slotBox.Items.Clear();
            for (int i = 0; i < 3; i++) slotBox.Items.Add((i + 1) + " · " + (model.Settings.Slots[i] == null ? "空白" : model.Settings.Slots[i].Name) + (model.Settings.StartupSlot == i ? " ★" : ""));
            slotBox.SelectedIndex = slot; updating = false;
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
            foreach (var g in model.Current.Groups.Concat(new[] { new GroupInfo { Id = "", Name = "未分組" } })) { var lg = new ListViewGroup(g.Name, HorizontalAlignment.Left); groups[g.Id] = lg; list.Groups.Add(lg); }
            foreach (var w in model.Windows) {
                var item = new ListViewItem(w.Title) { Tag = w, Group = groups[model.GroupOf(w) ?? ""] };
                item.SubItems.Add(w.AppName); item.SubItems.Add((w.Monitor ?? "").Replace("\\\\.\\", "")); item.SubItems.Add(model.GroupName(w));
                item.SubItems.Add(model.Hidden(w, true) ? "隱藏" : "顯示"); item.SubItems.Add(model.Hidden(w, false) ? "隱藏" : "顯示"); item.SubItems.Add(w.Minimized ? "最小化" : "開啟");
                var image = app.Icons.Get(w); if (image != null) { icons.Images.Add(w.Key, image); item.ImageKey = w.Key; }
                list.Items.Add(item); if (w.Key == selected) item.Selected = true;
            }
            list.EndUpdate();
            if (top != null) foreach (ListViewItem item in list.Items) if (((WindowInfo)item.Tag).Key == top) { list.TopItem = item; break; }
        }
        public void RefreshIcons()
        {
            if (!Visible) return;
            foreach (ListViewItem item in list.Items) {
                var w = (WindowInfo)item.Tag; if (icons.Images.ContainsKey(w.Key)) continue;
                var b = app.Icons.Get(w); if (b != null) { icons.Images.Add(w.Key, b); item.ImageKey = w.Key; }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { icons.Dispose(); context.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
