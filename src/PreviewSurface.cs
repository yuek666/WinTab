using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowTabManager
{
    internal sealed class PreviewCard
    {
        public WindowInfo Window;
        public Rectangle Bounds;
        public string GroupName;
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
        protected static readonly Color Background = Color.FromArgb(29, 31, 36), CardColor = Color.FromArgb(43, 46, 53), Muted = Color.FromArgb(173, 181, 193), Accent = Color.FromArgb(90, 165, 255);
        protected int ScrollOffset, ContentHeight, Columns = 1, SelectedIndex = -1;
        private int hoverIndex = -1;
        private readonly Font titleFont, smallFont;
        private readonly ToolTip tips = new ToolTip();
        private string tip = "";
        private OverviewBackdrop backdrop;
        protected abstract bool IsOverview { get; }
        protected abstract void Choose(WindowInfo window);
        protected abstract void Reflow();
        protected virtual void Cancel() { Hide(); }
        protected virtual void CloseSource(WindowInfo window) { }
        protected virtual void CardMenu(WindowInfo window, Point location) { }
        protected virtual void GroupMenu(PreviewGroup group, Point location) { }
        protected virtual void AddGroup() { }
        protected virtual void ToggleLayout() { }
        protected virtual bool Compact { get { return false; } }
        protected Rectangle Viewport { get { return new Rectangle(20, IsOverview ? 60 : 16, Math.Max(1, ClientSize.Width - 40), Math.Max(1, ClientSize.Height - (IsOverview ? 80 : 32))); } }
        private Rectangle ExitButton { get { return new Rectangle(Width - 56, 12, 36, 36); } }
        private Rectangle AddButton { get { return new Rectangle(Width - 100, 12, 36, 36); } }
        private Rectangle LayoutButton { get { return new Rectangle(Width - 144, 12, 36, 36); } }
        public int PreviewCount { get { return Thumbnails.Values.Count(t => t.Ready); } }
        public int CardCount { get { return Cards.Count; } }
        public int RegistrationCount { get { return Thumbnails.Count; } }
        internal int GroupHeaderCount { get { return Sections.Count; } }
        internal Rectangle CardRectangle(int index) { return CardBox(Cards[index]); }
        internal Rectangle CloseRectangle(int index) { return CloseBox(CardBox(Cards[index])); }
        internal string ThumbnailResults { get { return String.Join(", ", Thumbnails.Values.Select(t => "0x" + t.Result.ToString("X8"))); } }
        internal int RaiseError { get; private set; }
        internal int BackdropResult { get { return backdrop == null ? -1 : backdrop.BackdropResult; } }
        internal bool AcrylicEnabled { get { return backdrop != null && backdrop.AcrylicRequested; } }
        internal bool BackdropVisible { get { return backdrop != null && backdrop.Visible; } }

        protected PreviewSurface(IconCache icons)
        {
            Icons = icons; Theme.Apply(this); BackColor = Background; ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            titleFont = new Font(Font.FontFamily, 10); smallFont = new Font(Font.FontFamily, 9);
            AccessibleName = "視窗預覽切換器";
        }
        // Set the native TOPMOST style directly. Form.TopMost's CreateHandle path
        // invokes SetWindowPos without NOACTIVATE and steals focus from the source.
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x88; return cp; } }
        public void Present()
        {
            if (IsOverview) {
                EnsureBackdrop(); backdrop.Bounds = Bounds; backdrop.Show();
                Native.SetWindowPos(backdrop.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x40);
            }
            Show();
            bool raised = Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x40);
            RaiseError = raised ? 0 : Marshal.GetLastWin32Error();
            if (IsOverview) Activate();
        }
        private void ApplySurface()
        {
            if (!IsHandleCreated) return;
            if (!IsOverview) { SurfaceStyle.RoundWindow(this, 16); return; }
            EnsureBackdrop(); backdrop.Configure(); UpdateOverviewRegion();
            Invalidate();
        }
        private void EnsureBackdrop()
        {
            if (backdrop != null) return;
            backdrop = new OverviewBackdrop(); Owner = backdrop;
            backdrop.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Cancel(); };
            backdrop.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollContent(-e.Delta / 120 * 100); };
        }
        private void UpdateOverviewRegion()
        {
            if (!IsOverview || !IsHandleCreated) return;
            // Keep DWM thumbnail destinations opaque. Only the separate backdrop is
            // alpha blended, so transparent gaps never fade the preview pixels.
            var region = new Region(); region.MakeEmpty();
            foreach (var button in new[] { ExitButton, AddButton, LayoutButton }) using (var path = SurfaceStyle.Round(button, 8)) region.Union(path);
            foreach (var card in Cards) using (var path = SurfaceStyle.Round(CardBox(card), 12)) {
                using (var clipped = new Region(path)) { clipped.Intersect(Viewport); region.Union(clipped); }
            }
            foreach (var section in Sections) {
                int y = Viewport.Top + section.Y - ScrollOffset;
                int width = Math.Min(Width - 120, TextRenderer.MeasureText(section.Name + "  " + section.Count, titleFont).Width + 24);
                using (var path = SurfaceStyle.Round(new Rectangle(20, y, width, 30), 7))
                using (var clipped = new Region(path)) { clipped.Intersect(Viewport); region.Union(clipped); }
                if (section.Id != null) using (var path = SurfaceStyle.Round(new Rectangle(Width - 68, y, 40, 30), 7))
                    using (var clipped = new Region(path)) { clipped.Intersect(Viewport); region.Union(clipped); }
            }
            if (Cards.Count == 0 && Sections.Count == 0) region.Union(new Rectangle(Width / 2 - 120, Viewport.Top + Viewport.Height / 2 - 24, 240, 48));
            if (ContentHeight > Viewport.Height) region.Union(new Rectangle(Width - 12, Viewport.Top, 7, Viewport.Height));
            var old = Region; Region = region; if (old != null) old.Dispose();
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplySurface(); }
        protected override void WndProc(ref Message m)
        {
            // Mouse selection must not activate the Alt overlay before its target.
            if (!IsOverview && m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
            if ((m.Msg == 0x31E || m.Msg == 0x1A) && Visible) ApplySurface();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) {
                ApplySurface(); Reflow(); SyncPreviews();
                if (backdrop != null && !backdrop.Visible) { backdrop.Bounds = Bounds; backdrop.Show(); }
            } else { ReleasePreviews(); if (backdrop != null) backdrop.Hide(); }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!IsOverview) SurfaceStyle.RoundWindow(this, 16);
            if (backdrop != null) backdrop.Bounds = Bounds;
            if (Icons != null && Visible) { Reflow(); SyncPreviews(); Invalidate(); }
        }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (backdrop != null) backdrop.Bounds = Bounds; }
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
            UpdateOverviewRegion();
        }
        protected Rectangle CardBox(PreviewCard card) { var r = card.Bounds; r.Offset(0, Viewport.Top - ScrollOffset); return r; }
        private static Rectangle ImageBox(Rectangle box) { return new Rectangle(box.Left + 10, box.Top + 42, Math.Max(1, box.Width - 20), Math.Max(1, box.Height - 72)); }
        private static Rectangle CloseBox(Rectangle box) { return new Rectangle(box.Right - 36, box.Top + 7, 28, 28); }
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
        private static void Label(Graphics g, string text, Font font, Color color, Rectangle bounds, bool center = false)
        {
            if (bounds.Width < 1 || bounds.Height < 1) return;
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center, Alignment = center ? StringAlignment.Center : StringAlignment.Near })
                g.DrawString(text, font, brush, bounds, format);
        }
        private static void Cross(Graphics g, Rectangle r, Color color)
        {
            using (var pen = new Pen(color, 1.5f)) { g.DrawLine(pen, r.Left + 9, r.Top + 9, r.Right - 9, r.Bottom - 9); g.DrawLine(pen, r.Right - 9, r.Top + 9, r.Left + 9, r.Bottom - 9); }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (!IsOverview) using (var edge = SurfaceStyle.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 16))
                using (var pen = new Pen(Color.FromArgb(68, 73, 84))) g.DrawPath(pen, edge);
            if (IsOverview) {
                foreach (var box in new[] { ExitButton, AddButton, LayoutButton }) using (var path = SurfaceStyle.Round(box, 8)) using (var brush = new SolidBrush(CardColor)) g.FillPath(brush, path);
                Cross(g, Rectangle.Inflate(ExitButton, -4, -4), Muted);
                using (var pen = new Pen(Muted, 1.5f)) {
                    var r = AddButton; g.DrawLine(pen, r.Left + 11, r.Top + 18, r.Right - 11, r.Top + 18); g.DrawLine(pen, r.Left + 18, r.Top + 11, r.Left + 18, r.Bottom - 11);
                    r = LayoutButton;
                    if (Compact) for (int i = 0; i < 4; i++) g.DrawRectangle(pen, r.Left + 10 + i % 2 * 9, r.Top + 10 + i / 2 * 9, 6, 6);
                    else for (int i = 0; i < 3; i++) g.DrawLine(pen, r.Left + 10, r.Top + 11 + i * 7, r.Right - 10, r.Top + 11 + i * 7);
                }
            }
            var saved = g.Save(); g.SetClip(Viewport);
            foreach (var section in Sections) {
                int y = Viewport.Top + section.Y - ScrollOffset;
                Label(g, section.Name + "  " + section.Count, titleFont, Muted, new Rectangle(24, y, Width - 116, 30));
                if (section.Id != null) using (var dots = new SolidBrush(Muted)) for (int dot = 0; dot < 3; dot++) g.FillEllipse(dots, Width - 60 + dot * 8, y + 13, 3, 3);
            }
            for (int i = 0; i < Cards.Count; i++) {
                var card = Cards[i]; Rectangle box = CardBox(card);
                if (!box.IntersectsWith(Viewport)) continue;
                using (var path = SurfaceStyle.Round(Rectangle.Inflate(box, -1, -1), 12)) {
                    using (var brush = new SolidBrush(CardColor)) g.FillPath(brush, path);
                    using (var pen = new Pen(i == SelectedIndex ? Accent : i == hoverIndex ? Color.FromArgb(113, 125, 143) : Color.FromArgb(64, 69, 80), i == SelectedIndex ? 2.5f : 1)) g.DrawPath(pen, path);
                }
                var icon = Icons.Get(card.Window);
                if (icon != null) g.DrawImage(icon, new Rectangle(box.Left + 12, box.Top + 11, 20, 20));
                Label(g, card.Window.Title, titleFont, Color.White, new Rectangle(box.Left + 40, box.Top + 8, box.Width - 82, 28));
                var close = CloseBox(box);
                if (close.Contains(PointToClient(Cursor.Position))) using (var p = SurfaceStyle.Round(close, 5)) using (var b = new SolidBrush(Color.FromArgb(177, 63, 67))) g.FillPath(b, p);
                Cross(g, close, i == hoverIndex || i == SelectedIndex ? Color.White : Muted);
                Rectangle image = ImageBox(box);
                using (var brush = new SolidBrush(Color.FromArgb(21, 23, 28))) g.FillRectangle(brush, image);
                LiveThumbnail thumbnail;
                if (!Thumbnails.TryGetValue(card.Window.Key, out thumbnail) || !thumbnail.Ready)
                    Label(g, Native.IsWindow(card.Window.Handle) ? "預覽不可用" : "視窗已關閉", smallFont, Muted, image, true);
                string detail = card.GroupName ?? (card.Window.Monitor ?? "").Replace("\\\\.\\", "");
                if (card.Window.Minimized) detail += " · 最小化";
                Label(g, detail, smallFont, Muted, new Rectangle(box.Left + 12, box.Bottom - 27, box.Width - 24, 20));
            }
            if (Cards.Count == 0 && Sections.Count == 0) Label(g, "沒有可切換的視窗", titleFont, Muted, Viewport, true);
            g.Restore(saved);
            if (ContentHeight > Viewport.Height) {
                int height = Math.Max(24, Viewport.Height * Viewport.Height / ContentHeight);
                int y = Viewport.Top + ScrollOffset * (Viewport.Height - height) / Math.Max(1, ContentHeight - Viewport.Height);
                using (var brush = new SolidBrush(Color.FromArgb(105, 121, 141))) g.FillRectangle(brush, Width - 10, y, 3, height);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = Viewport.Contains(e.Location) ? Cards.FindIndex(c => CardBox(c).Contains(e.Location)) : -1;
            bool toolbar = IsOverview && (ExitButton.Contains(e.Location) || AddButton.Contains(e.Location) || LayoutButton.Contains(e.Location));
            string next = index >= 0 && CloseBox(CardBox(Cards[index])).Contains(e.Location) ? "關閉視窗" : IsOverview && ExitButton.Contains(e.Location) ? "關閉總覽" : IsOverview && AddButton.Contains(e.Location) ? "新增群組" : IsOverview && LayoutButton.Contains(e.Location) ? (Compact ? "切換為分組排列" : "切換為原生並排") : "";
            if (tip != next) { tip = next; tips.SetToolTip(this, tip); }
            if (hoverIndex != index || index >= 0) { hoverIndex = index; Invalidate(); }
            Cursor = index >= 0 || toolbar ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverIndex = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); ClickAt(e.Location, e.Button); }
        internal void ClickAt(Point location, MouseButtons button)
        {
            if (IsOverview && button == MouseButtons.Left) {
                if (ExitButton.Contains(location)) { Cancel(); return; }
                if (AddButton.Contains(location)) { AddGroup(); return; }
                if (LayoutButton.Contains(location)) { ToggleLayout(); return; }
            }
            if (!Viewport.Contains(location)) return;
            int index = Cards.FindIndex(c => CardBox(c).Contains(location));
            if (index >= 0) {
                if (button == MouseButtons.Left && CloseBox(CardBox(Cards[index])).Contains(location)) { CloseSource(Cards[index].Window); return; }
                SelectedIndex = index; Invalidate();
                if (button == MouseButtons.Left) Choose(Cards[index].Window);
                else if (button == MouseButtons.Right) CardMenu(Cards[index].Window, location);
                return;
            }
            foreach (var section in Sections) {
                var header = new Rectangle(20, Viewport.Top + section.Y - ScrollOffset, Width - 40, 34);
                if (header.Contains(location) && (button == MouseButtons.Right || location.X > Width - 85)) { GroupMenu(section, location); return; }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollContent(-e.Delta / 120 * 100); }
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
            if (disposing) {
                ReleasePreviews(); tips.Dispose(); titleFont.Dispose(); smallFont.Dispose(); Font.Dispose();
                if (backdrop != null) { Owner = null; backdrop.Dispose(); backdrop = null; }
            }
            base.Dispose(disposing);
        }
    }
    internal sealed class OverviewBackdrop : Form
    {
        internal int BackdropResult { get; private set; }
        internal bool AcrylicRequested { get; private set; }
        internal OverviewBackdrop()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 26, 32); Opacity = 0.64;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000088; return cp; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Configure(); }
        internal void Configure()
        {
            if (!IsHandleCreated) return;
            bool transparency = !SystemInformation.HighContrast;
            try { transparency &= Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1)) != 0; }
            catch (System.Security.SecurityException) { transparency = false; }
            Opacity = transparency ? 0.64 : 1.0;
            BackdropResult = SurfaceStyle.Acrylic(Handle, transparency);
            AcrylicRequested = transparency && BackdropResult == 0; Invalidate();
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (AcrylicRequested) SurfaceStyle.ClearGlass(e.Graphics, ClientRectangle); else base.OnPaintBackground(e);
        }
    }
    internal sealed class AltSwitcher : PreviewSurface
    {
        private readonly Action<WindowInfo> choose, close;
        public WindowInfo[] Candidates { get; private set; }
        public int Index { get { return SelectedIndex; } }
        protected override bool IsOverview { get { return false; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }
        public AltSwitcher(WindowInfo[] candidates, SwitchCommand command, Action<WindowInfo> choose, IconCache icons, Action<WindowInfo> close = null) : base(icons)
        {
            Candidates = candidates; this.choose = choose; this.close = close;
            Text = "WindowTabManager — Alt+Tab";
            var work = command.Monitor.Work.Bounds;
            int width = Math.Max(280, Math.Min(work.Width - 64, Math.Min(1180, Math.Max(360, candidates.Length * 296 + 40))));
            int columns = Math.Max(1, (width - 40) / 290);
            int rows = Math.Max(1, (candidates.Length + columns - 1) / columns);
            int height = Math.Max(240, Math.Min(work.Height - 80, Math.Min(740, 40 + rows * 224)));
            Bounds = new Rectangle(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
            SelectedIndex = InitialIndex(candidates.Length, Array.FindIndex(candidates, w => w.Handle == command.Foreground), command.Reverse); Reflow();
        }
        internal static int InitialIndex(int count, int current, bool reverse)
        {
            if (count == 0) return -1;
            if (current < 0) return reverse ? count - 1 : 0;
            return (current + (reverse ? -1 : 1) + count) % count;
        }
        public void UpdateCandidates(WindowInfo[] live)
        {
            string selected = Index >= 0 && Index < Candidates.Length ? Candidates[Index].Key : null;
            var byKey = live.ToDictionary(w => w.Key);
            // Keep the opening order and monitor scope; only remove dead identities/update labels.
            Candidates = Candidates.Where(w => byKey.ContainsKey(w.Key)).Select(w => byKey[w.Key]).ToArray();
            int next = Array.FindIndex(Candidates, w => w.Key == selected);
            SelectedIndex = Candidates.Length == 0 ? -1 : next >= 0 ? next : Math.Min(Math.Max(0, Index), Candidates.Length - 1);
            Reflow(); EnsureSelection(); RefreshSources();
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
        protected override void CloseSource(WindowInfo window) { if (close != null) close(window); }
        public void Step(bool reverse) { SelectRelative(reverse ? -1 : 1); }
        public void Commit() { if (Index >= 0 && Index < Candidates.Length) choose(Candidates[Index]); }
        public void RefreshIcons(IconCache icons) { RefreshIcons(); }
    }
    internal sealed class OverviewSwitcher : PreviewSurface
    {
        private readonly AppController app;
        private string signature;
        private DarkMenu popup;
        protected override bool IsOverview { get { return true; } }
        protected override bool Compact { get { return app != null && app.Model.Settings.CompactOverview; } }
        public OverviewSwitcher(AppController app) : base(app.Icons) { this.app = app; Text = "WindowTabManager — Win+Tab"; }
        public void RefreshView(bool force)
        {
            if (!Visible) return;
            var model = app.Model;
            string next = Compact + String.Join("\n", model.Windows.Select(w => w.Key + w.Title + w.Monitor + w.Minimized + model.GroupOf(w) + model.Hidden(w, false))) + String.Join("|", model.Current.Groups.Select(g => g.Id + g.Name));
            if (force || signature != next) { signature = next; Reflow(); }
            SyncPreviews(); Invalidate();
        }
        protected override void Reflow()
        {
            if (app == null) return;
            string selected = SelectedIndex >= 0 && SelectedIndex < Cards.Count ? Cards[SelectedIndex].Window.Key : null;
            Cards.Clear(); Sections.Clear();
            Columns = Math.Max(1, Viewport.Width / 330);
            int width = Math.Min(400, (Viewport.Width - (Columns - 1) * 20) / Columns);
            int height = Math.Min(280, Math.Max(208, width * 9 / 16 + 72));
            int y = 8;
            if (Compact) {
                var windows = app.Model.Windows.Where(w => !app.Model.Hidden(w, false)).ToArray();
                int startX = 20 + Math.Max(0, (Viewport.Width - Math.Min(Columns, windows.Length) * (width + 20) + 20) / 2);
                for (int i = 0; i < windows.Length; i++) Cards.Add(new PreviewCard { Window = windows[i], GroupName = app.Model.GroupName(windows[i]), Bounds = new Rectangle(startX + i % Columns * (width + 20), y + i / Columns * (height + 20), width, height) });
                y += ((windows.Length + Columns - 1) / Columns) * (height + 20);
            } else foreach (var group in app.Model.Current.Groups.Concat(new[] { new GroupInfo { Name = "未分組" } })) {
                var windows = app.Model.Windows.Where(w => app.Model.GroupOf(w) == group.Id && !app.Model.Hidden(w, false)).ToArray();
                if (group.Id == null && windows.Length == 0 && app.Model.Current.Groups.Count > 0) continue;
                Sections.Add(new PreviewGroup { Id = group.Id, Name = group.Name, Y = y, Count = windows.Length }); y += 38;
                for (int i = 0; i < windows.Length; i++) Cards.Add(new PreviewCard { Window = windows[i], Bounds = new Rectangle(20 + i % Columns * (width + 20), y + i / Columns * (height + 20), width, height) });
                y += Math.Max(1, (windows.Length + Columns - 1) / Columns) * (windows.Length == 0 ? 36 : height + 20) + 12;
            }
            ContentHeight = y;
            SelectedIndex = Cards.FindIndex(c => c.Window.Key == selected);
            if (SelectedIndex < 0 && Cards.Count > 0) SelectedIndex = 0;
        }
        protected override void Choose(WindowInfo window) { app.FocusWindow(window); }
        protected override void CloseSource(WindowInfo window) { app.RequestCloseWindow(window); }
        protected override void Cancel() { Hide(); app.PanelClosed(); }
        protected override void ToggleLayout() { ScrollOffset = 0; app.SetOverviewMode(!Compact); }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (!app.Exiting) { e.Cancel = true; Cancel(); } base.OnFormClosing(e); }
        protected override void AddGroup()
        {
            string name = app.Prompt(this, "新增群組", "新群組");
            if (name != null) { app.Model.AddGroup(name); app.Changed(); }
        }
        private DarkMenu NewMenu() { if (popup != null) popup.Dispose(); popup = new DarkMenu(); return popup; }
        protected override void CardMenu(WindowInfo window, Point location)
        {
            app.PanelClosed();
            var menu = NewMenu();
            menu.Items.Add("切換至此視窗", null, delegate { Choose(window); });
            var move = new ToolStripMenuItem("移至群組");
            move.DropDownItems.Add("未分組", null, delegate { app.Model.Move(window, null); app.Changed(); });
            foreach (var g in app.Model.Current.Groups) { var group = g; move.DropDownItems.Add(group.Name, null, delegate { app.Model.Move(window, group.Id); app.Changed(); }); }
            menu.Items.Add(move);
            menu.Items.Add("新增群組並移入", null, delegate { string name = app.Prompt(this, "新增群組", "新群組"); if (name != null) { var group = app.Model.AddGroup(name); app.Model.Move(window, group.Id); app.Changed(); } });
            var current = app.Model.Current.Groups.FirstOrDefault(g => g.Id == app.Model.GroupOf(window));
            if (current != null) AddGroupCommands(menu, current);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("從 Win+Tab 隱藏", null, delegate { app.SetHidden(window, false, true); });
            menu.Show(this, location);
        }
        private void AddGroupCommands(DarkMenu menu, GroupInfo group)
        {
            menu.Items.Add("重新命名群組", null, delegate { var name = app.Prompt(this, "重新命名群組", group.Name); if (name != null) { group.Name = name; app.Model.Dirty = true; app.Changed(); } });
            menu.Items.Add("刪除群組（視窗移至未分組）", null, delegate { app.Model.DeleteGroup(group); app.Changed(); });
        }
        protected override void GroupMenu(PreviewGroup section, Point location)
        {
            var group = app.Model.Current.Groups.FirstOrDefault(g => g.Id == section.Id);
            if (group == null) return;
            app.PanelClosed(); var menu = NewMenu(); AddGroupCommands(menu, group); menu.Show(this, location);
        }
        protected override void Dispose(bool disposing) { if (disposing && popup != null) popup.Dispose(); base.Dispose(disposing); }
    }
}
