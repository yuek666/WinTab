using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowTabManager
{
    // DWM owns the live pixels. No bitmap capture or recurring preview timer is used.
    internal sealed class LiveThumbnail : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct SourceSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)] private struct Properties
        {
            public uint Flags;
            public Native.Rect Destination, Source;
            public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
        }
        [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
        [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SourceSize size);
        [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref Properties properties);
        private IntPtr handle;
        public bool Ready { get; private set; }
        public int Result { get; private set; }
        public static int ActiveCount { get; private set; }
        public LiveThumbnail(IntPtr destination, IntPtr source)
        {
            Result = DwmRegisterThumbnail(destination, source, out handle);
            if (Result == 0 && handle != IntPtr.Zero) ActiveCount++;
        }
        internal static Rectangle Fit(Size source, Rectangle area)
        {
            if (source.Width <= 0 || source.Height <= 0 || area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;
            double scale = Math.Min((double)area.Width / source.Width, (double)area.Height / source.Height);
            int width = Math.Max(1, (int)(source.Width * scale)), height = Math.Max(1, (int)(source.Height * scale));
            return new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        }
        private static Native.Rect Rect(Rectangle r) { return new Native.Rect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom }; }
        public void Place(Rectangle area, Rectangle viewport)
        {
            Ready = false;
            if (handle == IntPtr.Zero) return;
            SourceSize source;
            Result = DwmQueryThumbnailSourceSize(handle, out source);
            if (Result != 0 || source.Width <= 0 || source.Height <= 0) { Hide(); return; }
            Rectangle full = Fit(new Size(source.Width, source.Height), area);
            Rectangle visible = Rectangle.Intersect(full, viewport);
            if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) { Hide(); return; }
            var crop = Rectangle.FromLTRB(
                (int)((long)(visible.Left - full.Left) * source.Width / full.Width),
                (int)((long)(visible.Top - full.Top) * source.Height / full.Height),
                (int)((long)(visible.Right - full.Left) * source.Width / full.Width),
                (int)((long)(visible.Bottom - full.Top) * source.Height / full.Height));
            var properties = new Properties { Flags = 0x1F, Destination = Rect(visible), Source = Rect(crop), Opacity = 255, Visible = true, ClientOnly = false };
            Result = DwmUpdateThumbnailProperties(handle, ref properties); Ready = Result == 0;
        }
        private void Hide()
        {
            var properties = new Properties { Flags = 8, Visible = false };
            DwmUpdateThumbnailProperties(handle, ref properties);
        }
        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            DwmUnregisterThumbnail(handle); handle = IntPtr.Zero; Ready = false; ActiveCount--;
        }
    }

    internal sealed class PreviewCard
    {
        public WindowInfo Window;
        public Rectangle Bounds;
    }
    internal sealed class PreviewGroup
    {
        public string Id, Name;
        public int Y, Count;
    }

    internal abstract class PreviewSurface : Form
    {
        protected readonly IconCache Icons;
        protected readonly List<PreviewCard> Cards = new List<PreviewCard>();
        protected readonly List<PreviewGroup> Sections = new List<PreviewGroup>();
        protected readonly Dictionary<string, LiveThumbnail> Thumbnails = new Dictionary<string, LiveThumbnail>();
        protected static readonly Color Background = Color.FromArgb(24, 28, 35), CardColor = Color.FromArgb(37, 43, 52), Muted = Color.FromArgb(165, 179, 195), Accent = Color.FromArgb(117, 184, 255);
        protected int ScrollOffset, ContentHeight, Columns = 1, SelectedIndex = -1;
        protected int HeaderHeight = 100;
        private int hoverIndex = -1;
        private readonly Font headingFont, titleFont, smallFont;
        protected abstract bool IsOverview { get; }
        protected abstract string Heading { get; }
        protected abstract string Hint { get; }
        protected abstract void Choose(WindowInfo window);
        protected abstract void Reflow();
        protected virtual void Cancel() { Hide(); }
        protected virtual void CardMenu(WindowInfo window, Point location) { }
        protected virtual void GroupMenu(PreviewGroup group, Point location) { }
        protected virtual void AddGroup() { }
        protected Rectangle Viewport { get { return new Rectangle(20, HeaderHeight, Math.Max(1, ClientSize.Width - 40), Math.Max(1, ClientSize.Height - HeaderHeight - 42)); } }
        public int PreviewCount { get { return Thumbnails.Values.Count(t => t.Ready); } }
        public int CardCount { get { return Cards.Count; } }
        public int RegistrationCount { get { return Thumbnails.Count; } }
        internal string ThumbnailResults { get { return String.Join(", ", Thumbnails.Values.Select(t => "0x" + t.Result.ToString("X8"))); } }
        protected PreviewSurface(IconCache icons)
        {
            Icons = icons; Theme.Apply(this); BackColor = Background; ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            headingFont = new Font(Font.FontFamily, 20, FontStyle.Bold); titleFont = new Font(Font.FontFamily, 11, FontStyle.Regular); smallFont = new Font(Font.FontFamily, 9);
            AccessibleName = "視窗預覽切換器";
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
        internal int RaiseError { get; private set; }
        public void Present()
        {
            Show();
            // Reassert Z order on every opening, including a reused overview. Merely
            // retaining WS_EX_TOPMOST does not order us above another topmost window.
            bool raised = Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x40);
            RaiseError = raised ? 0 : Marshal.GetLastWin32Error();
            if (IsOverview) Activate();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { Reflow(); SyncPreviews(); } else ReleasePreviews();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Icons != null && Visible) { Reflow(); SyncPreviews(); Invalidate(); }
        }
        protected override void OnHandleDestroyed(EventArgs e) { ReleasePreviews(); base.OnHandleDestroyed(e); }
        protected void ReleasePreviews()
        {
            foreach (var thumbnail in Thumbnails.Values) thumbnail.Dispose();
            Thumbnails.Clear();
        }
        protected void SyncPreviews()
        {
            if (!Visible || !IsHandleCreated) { ReleasePreviews(); return; }
            ScrollOffset = Math.Max(0, Math.Min(ScrollOffset, Math.Max(0, ContentHeight - Viewport.Height)));
            var visible = new HashSet<string>();
            foreach (var card in Cards) {
                Rectangle box = CardBox(card);
                if (!box.IntersectsWith(Viewport)) continue;
                Icons.Get(card.Window);
                if (!Native.IsWindow(card.Window.Handle)) continue;
                visible.Add(card.Window.Key);
                LiveThumbnail thumbnail;
                if (!Thumbnails.TryGetValue(card.Window.Key, out thumbnail)) {
                    if (!Native.StillSame(card.Window)) continue;
                    thumbnail = new LiveThumbnail(Handle, card.Window.Handle); Thumbnails[card.Window.Key] = thumbnail;
                }
                thumbnail.Place(ImageBox(box), Viewport);
            }
            foreach (string key in Thumbnails.Keys.Where(k => !visible.Contains(k)).ToArray()) { Thumbnails[key].Dispose(); Thumbnails.Remove(key); }
        }
        protected Rectangle CardBox(PreviewCard card) { var r = card.Bounds; r.Offset(0, Viewport.Top - ScrollOffset); return r; }
        private static Rectangle ImageBox(Rectangle box) { return new Rectangle(box.Left + 10, box.Top + 44, Math.Max(1, box.Width - 20), Math.Max(1, box.Height - 78)); }
        protected void EnsureSelection()
        {
            if (SelectedIndex < 0 || SelectedIndex >= Cards.Count) return;
            var r = Cards[SelectedIndex].Bounds;
            if (r.Top < ScrollOffset) ScrollOffset = r.Top;
            if (r.Bottom > ScrollOffset + Viewport.Height) ScrollOffset = r.Bottom - Viewport.Height;
            SyncPreviews(); Invalidate();
        }
        protected void SelectRelative(int delta)
        {
            if (Cards.Count == 0) return;
            SelectedIndex = (Math.Max(0, SelectedIndex) + delta % Cards.Count + Cards.Count) % Cards.Count; EnsureSelection();
        }
        public void RefreshIcons() { if (Visible) { foreach (var card in Cards.Where(c => CardBox(c).IntersectsWith(Viewport))) Icons.Get(card.Window); Invalidate(); } }
        public void RefreshSources() { if (Visible) { SyncPreviews(); Invalidate(); } }
        private void DrawLabel(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags extra)
        {
            if (bounds.Width > 0 && bounds.Height > 0) TextRenderer.DrawText(g, text, font, bounds, color, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | extra);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            using (var border = new Pen(Color.FromArgb(62, 73, 87))) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            DrawLabel(g, Heading, headingFont, Color.White, new Rectangle(28, 23, Math.Max(1, Width - (IsOverview ? 240 : 50)), 38), TextFormatFlags.VerticalCenter);
            DrawLabel(g, Hint, smallFont, Muted, new Rectangle(30, 66, Width - 60, 25), TextFormatFlags.VerticalCenter);
            if (IsOverview) {
                DrawLabel(g, "+ 新增群組", titleFont, Accent, new Rectangle(Width - 212, 28, 140, 30), TextFormatFlags.VerticalCenter);
                using (var pen = new Pen(Muted, 2)) { g.DrawLine(pen, Width - 47, 34, Width - 33, 48); g.DrawLine(pen, Width - 33, 34, Width - 47, 48); }
            }
            var saved = g.Save(); g.SetClip(Viewport);
            foreach (var section in Sections) {
                int y = Viewport.Top + section.Y - ScrollOffset;
                DrawLabel(g, section.Name + "   " + section.Count, titleFont, Muted, new Rectangle(28, y, Width - 116, 30), TextFormatFlags.VerticalCenter);
                if (section.Id != null) using (var dots = new SolidBrush(Muted)) for (int dot = 0; dot < 3; dot++) g.FillEllipse(dots, Width - 60 + dot * 8, y + 13, 3, 3);
            }
            for (int i = 0; i < Cards.Count; i++) {
                var card = Cards[i]; Rectangle box = CardBox(card);
                if (!box.IntersectsWith(Viewport)) continue;
                using (var brush = new SolidBrush(CardColor)) g.FillRectangle(brush, box);
                using (var pen = new Pen(i == SelectedIndex ? Accent : i == hoverIndex ? Color.FromArgb(101, 119, 143) : Color.FromArgb(53, 62, 74), i == SelectedIndex ? 3 : 1))
                    g.DrawRectangle(pen, box.Left + 1, box.Top + 1, box.Width - 3, box.Height - 3);
                var icon = Icons.Get(card.Window);
                if (icon != null) g.DrawImage(icon, new Rectangle(box.Left + 13, box.Top + 12, 22, 22));
                DrawLabel(g, card.Window.Title, titleFont, Color.White, new Rectangle(box.Left + 42, box.Top + 9, box.Width - 52, 28), TextFormatFlags.VerticalCenter);
                Rectangle image = ImageBox(box);
                using (var brush = new SolidBrush(Color.FromArgb(17, 21, 27))) g.FillRectangle(brush, image);
                LiveThumbnail thumbnail;
                if (!Thumbnails.TryGetValue(card.Window.Key, out thumbnail) || !thumbnail.Ready)
                    DrawLabel(g, Native.IsWindow(card.Window.Handle) ? "無法取得視窗預覽" : "視窗已關閉或不可用", smallFont, Muted, image, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                string detail = card.Window.Minimized ? "最小化 · 預覽可能暫停" : card.Window.AppName + " · " + (card.Window.Monitor ?? "").Replace("\\\\.\\", "");
                DrawLabel(g, detail, smallFont, Muted, new Rectangle(box.Left + 12, box.Bottom - 30, box.Width - 24, 24), TextFormatFlags.VerticalCenter);
            }
            if (Cards.Count == 0 && Sections.Count == 0) DrawLabel(g, "沒有可切換的視窗", titleFont, Muted, Viewport, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.Restore(saved);
            DrawLabel(g, IsOverview ? "點預覽切換  ·  右鍵管理群組  ·  方向鍵選取  ·  Esc 關閉" : "Tab 下一個  ·  Shift+Tab 上一個  ·  放開 Alt 切換  ·  Esc 取消",
                smallFont, Muted, new Rectangle(28, Height - 36, Width - 56, 28), TextFormatFlags.VerticalCenter);
            if (ContentHeight > Viewport.Height) {
                int height = Math.Max(24, Viewport.Height * Viewport.Height / ContentHeight);
                int y = Viewport.Top + ScrollOffset * (Viewport.Height - height) / Math.Max(1, ContentHeight - Viewport.Height);
                using (var brush = new SolidBrush(Color.FromArgb(105, 121, 141))) g.FillRectangle(brush, Width - 12, y, 4, height);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = Viewport.Contains(e.Location) ? Cards.FindIndex(c => CardBox(c).Contains(e.Location)) : -1;
            if (hoverIndex != index) { hoverIndex = index; Invalidate(); }
            Cursor = index >= 0 || (IsOverview && e.Y < 62 && e.X > Width - 215) ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverIndex = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (IsOverview && e.Button == MouseButtons.Left && e.Y < 64) {
                if (e.X >= Width - 64) { Cancel(); return; }
                if (e.X >= Width - 220) { AddGroup(); return; }
            }
            if (!Viewport.Contains(e.Location)) return;
            int index = Cards.FindIndex(c => CardBox(c).Contains(e.Location));
            if (index >= 0) {
                SelectedIndex = index; Invalidate();
                if (e.Button == MouseButtons.Left) Choose(Cards[index].Window);
                else if (e.Button == MouseButtons.Right) CardMenu(Cards[index].Window, e.Location);
                return;
            }
            foreach (var section in Sections) {
                var header = new Rectangle(20, Viewport.Top + section.Y - ScrollOffset, Width - 40, 34);
                if (header.Contains(e.Location) && (e.Button == MouseButtons.Right || e.X > Width - 85)) { GroupMenu(section, e.Location); return; }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e); ScrollContent(-e.Delta / 120 * 100);
        }
        internal void ScrollContent(int pixels) { ScrollOffset += pixels; SyncPreviews(); Invalidate(); }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { Cancel(); return true; }
            if (IsOverview) {
                if (key == Keys.Enter && SelectedIndex >= 0 && SelectedIndex < Cards.Count) { Choose(Cards[SelectedIndex].Window); return true; }
                if (key == Keys.Tab || key == Keys.Right || key == Keys.Left || key == Keys.Up || key == Keys.Down) {
                    SelectRelative(key == Keys.Left ? -1 : key == Keys.Up ? -Columns : key == Keys.Down ? Columns : key == Keys.Tab && (keyData & Keys.Shift) != 0 ? -1 : 1); return true;
                }
            }
            return base.ProcessDialogKey(keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { ReleasePreviews(); headingFont.Dispose(); titleFont.Dispose(); smallFont.Dispose(); Font.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class AltSwitcher : PreviewSurface
    {
        private readonly Action<WindowInfo> choose;
        private readonly string monitorName;
        public WindowInfo[] Candidates { get; private set; }
        public int Index { get { return SelectedIndex; } }
        protected override bool IsOverview { get { return false; } }
        protected override string Heading { get { return "切換視窗"; } }
        protected override string Hint { get { return monitorName + "  ·  " + Candidates.Length + " 個視窗"; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }
        public AltSwitcher(WindowInfo[] candidates, SwitchCommand command, Action<WindowInfo> choose, IconCache icons) : base(icons)
        {
            Candidates = candidates; this.choose = choose; monitorName = (command.Monitor.Device ?? "").Replace("\\\\.\\", "");
            Text = "WindowTabManager — Alt+Tab 預覽";
            var work = command.Monitor.Work.Bounds;
            int width = Math.Max(280, Math.Min(work.Width - 64, Math.Min(1180, Math.Max(430, candidates.Length * 296 + 42))));
            int columns = Math.Max(1, (width - 40) / 290);
            int rows = Math.Max(1, (candidates.Length + columns - 1) / columns);
            int height = Math.Max(260, Math.Min(work.Height - 80, Math.Min(760, 156 + rows * 224)));
            Bounds = new Rectangle(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
            SelectedIndex = InitialIndex(candidates.Length, Array.FindIndex(candidates, w => w.Handle == command.Foreground), command.Reverse);
            Reflow();
        }
        internal static int InitialIndex(int count, int current, bool reverse)
        {
            if (count == 0) return -1;
            if (current < 0) return reverse ? count - 1 : 0;
            return (current + (reverse ? -1 : 1) + count) % count;
        }
        protected override void Reflow()
        {
            if (Candidates == null) return;
            Cards.Clear(); Columns = Math.Max(1, Viewport.Width / 290);
            int width = (Viewport.Width - (Columns - 1) * 16) / Columns;
            for (int i = 0; i < Candidates.Length; i++) Cards.Add(new PreviewCard { Window = Candidates[i], Bounds = new Rectangle(20 + i % Columns * (width + 16), i / Columns * 224 + 4, width, 208) });
            ContentHeight = ((Candidates.Length + Columns - 1) / Columns) * 224 + 8;
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); EnsureSelection(); }
        protected override void Choose(WindowInfo window) { choose(window); }
        public void Step(bool reverse) { SelectRelative(reverse ? -1 : 1); }
        public void Commit() { if (Index >= 0 && Index < Candidates.Length) choose(Candidates[Index]); }
        public void RefreshIcons(IconCache icons) { RefreshIcons(); }
    }

    internal sealed class OverviewSwitcher : PreviewSurface
    {
        private readonly AppController app;
        private string signature;
        private ContextMenuStrip popup;
        protected override bool IsOverview { get { return true; } }
        protected override string Heading { get { return "視窗總覽"; } }
        protected override string Hint { get { return "所有螢幕  ·  " + Cards.Count + " 個視窗  ·  " + (app.Model.Dirty ? "分組尚未儲存" : "目前分組"); } }
        public OverviewSwitcher(AppController app) : base(app.Icons) { this.app = app; Text = "WindowTabManager — 視窗總覽"; }
        public void RefreshView(bool force)
        {
            if (!Visible) return;
            var model = app.Model;
            string next = String.Join("\n", model.Windows.Select(w => w.Key + w.Title + w.Monitor + w.Minimized + model.GroupOf(w) + model.Hidden(w, false))) + String.Join("|", model.Current.Groups.Select(g => g.Id + g.Name));
            if (force || signature != next) { signature = next; Reflow(); }
            SyncPreviews();
            Invalidate();
        }
        protected override void Reflow()
        {
            if (app == null) return;
            string selected = SelectedIndex >= 0 && SelectedIndex < Cards.Count ? Cards[SelectedIndex].Window.Key : null;
            Cards.Clear(); Sections.Clear();
            Columns = Math.Max(1, Viewport.Width / 330);
            int width = Math.Min(400, (Viewport.Width - (Columns - 1) * 20) / Columns);
            int height = Math.Min(280, Math.Max(208, width * 9 / 16 + 78));
            int y = 8;
            foreach (var group in app.Model.Current.Groups.Concat(new[] { new GroupInfo { Name = "未分組" } })) {
                var windows = app.Model.Windows.Where(w => app.Model.GroupOf(w) == group.Id && !app.Model.Hidden(w, false)).ToArray();
                if (group.Id == null && windows.Length == 0 && app.Model.Current.Groups.Count > 0) continue;
                Sections.Add(new PreviewGroup { Id = group.Id, Name = group.Name, Y = y, Count = windows.Length });
                y += 42;
                for (int i = 0; i < windows.Length; i++) Cards.Add(new PreviewCard { Window = windows[i], Bounds = new Rectangle(20 + i % Columns * (width + 20), y + i / Columns * (height + 20), width, height) });
                y += Math.Max(1, (windows.Length + Columns - 1) / Columns) * (windows.Length == 0 ? 36 : height + 20) + 16;
            }
            ContentHeight = y;
            SelectedIndex = Cards.FindIndex(c => c.Window.Key == selected);
            if (SelectedIndex < 0 && Cards.Count > 0) SelectedIndex = 0;
        }
        protected override void Choose(WindowInfo window) { app.FocusWindow(window); }
        protected override void Cancel() { Hide(); app.PanelClosed(); }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (!app.Exiting) { e.Cancel = true; Cancel(); } base.OnFormClosing(e); }
        protected override void AddGroup()
        {
            string name = app.Prompt(this, "新增群組", "新群組");
            if (name != null) { app.Model.AddGroup(name); app.Changed(); }
        }
        private ContextMenuStrip NewMenu()
        {
            if (popup != null) popup.Dispose();
            popup = new ContextMenuStrip(); return popup;
        }
        protected override void CardMenu(WindowInfo window, Point location)
        {
            app.PanelClosed(); // Escape belongs to the context menu while it is open.
            var menu = NewMenu();
            menu.Items.Add("切換至此視窗", null, delegate { Choose(window); });
            var move = new ToolStripMenuItem("移至群組");
            move.DropDownItems.Add("未分組", null, delegate { app.Model.Move(window, null); app.Changed(); });
            foreach (var g in app.Model.Current.Groups) { var group = g; move.DropDownItems.Add(group.Name, null, delegate { app.Model.Move(window, group.Id); app.Changed(); }); }
            menu.Items.Add(move);
            menu.Items.Add("新增群組並移入", null, delegate {
                string name = app.Prompt(this, "新增群組", "新群組");
                if (name != null) { var group = app.Model.AddGroup(name); app.Model.Move(window, group.Id); app.Changed(); }
            });
            menu.Items.Add("從 Win+Tab 隱藏", null, delegate { app.SetHidden(window, false, true); });
            menu.Show(this, location);
        }
        protected override void GroupMenu(PreviewGroup section, Point location)
        {
            var group = app.Model.Current.Groups.FirstOrDefault(g => g.Id == section.Id);
            if (group == null) return;
            app.PanelClosed(); var menu = NewMenu();
            menu.Items.Add("重新命名群組", null, delegate { var name = app.Prompt(this, "重新命名群組", group.Name); if (name != null) { group.Name = name; app.Model.Dirty = true; app.Changed(); } });
            menu.Items.Add("刪除群組（視窗移至未分組）", null, delegate { app.Model.DeleteGroup(group); app.Changed(); });
            menu.Show(this, location);
        }
        protected override void Dispose(bool disposing) { if (disposing && popup != null) popup.Dispose(); base.Dispose(disposing); }
    }
}
