using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowTabManager
{
    internal static class SurfaceStyle
    {
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Native.Rect rect, IntPtr brush);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
        internal static void ClearGlass(Graphics graphics, Rectangle area)
        {
            // GDI black writes zero alpha for DWM's extended client area.
            IntPtr dc = graphics.GetHdc();
            try { var rect = new Native.Rect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom }; FillRect(dc, ref rect, GetStockObject(4)); }
            finally { graphics.ReleaseHdc(dc); }
        }
        internal static GraphicsPath Round(RectangleF box, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
            if (diameter <= 0) { path.AddRectangle(box); return path; }
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void RoundWindow(Control control, int radius)
        {
            if (control.Width < 1 || control.Height < 1) return;
            using (var path = Round(new Rectangle(0, 0, control.Width, control.Height), radius)) {
                var old = control.Region; control.Region = new Region(path); if (old != null) old.Dispose();
            }
        }
        internal static int Acrylic(IntPtr hwnd, bool enabled)
        {
            // Documented Windows 11 22H2 API; unsupported systems retain opaque UI.
            int dark = 1, backdrop = enabled ? 3 : 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            int result = DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
            var margins = new Margins { Left = enabled && result == 0 ? -1 : 0 };
            int frameResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            return result != 0 ? result : frameResult;
        }
    }

    internal sealed class DarkMenu : ContextMenuStrip
    {
        public DarkMenu()
        {
            Font = new Font("Microsoft JhengHei UI", 10);
            Padding = new Padding(5); ShowImageMargin = true; BackColor = Color.FromArgb(32, 34, 39); ForeColor = Color.FromArgb(239, 241, 246);
            Renderer = new DarkMenuRenderer();
        }
        internal void StyleItems()
        {
            Style(this);
        }
        private void Style(ToolStrip strip)
        {
            strip.Renderer = Renderer; strip.BackColor = BackColor; strip.ForeColor = ForeColor;
            foreach (ToolStripItem item in strip.Items) {
                item.ForeColor = ForeColor; item.Padding = new Padding(5, 5, 8, 5);
                var child = item as ToolStripMenuItem;
                if (child != null && child.HasDropDownItems) Style(child.DropDown);
            }
        }
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            base.OnOpening(e); StyleItems();
        }
        protected override void OnOpened(EventArgs e) { SurfaceStyle.RoundWindow(this, 8); base.OnOpened(e); }
    }
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(239, 241, 246) : Color.FromArgb(130, 137, 148); base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Color.FromArgb(211, 217, 228) : Color.Gray; base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var pen = new Pen(Color.FromArgb(119, 185, 255), 2))
                e.Graphics.DrawLines(pen, new[] { new Point(r.Left + 2, r.Top + r.Height / 2), new Point(r.Left + 6, r.Bottom - 4), new Point(r.Right - 1, r.Top + 3) });
        }
    }
    internal sealed class DarkMenuColors : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(32, 34, 39), Line = Color.FromArgb(60, 64, 72), Hover = Color.FromArgb(54, 59, 68);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuItemSelected { get { return Hover; } }
        public override Color MenuItemBorder { get { return Hover; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemPressedGradientBegin { get { return Hover; } }
        public override Color MenuItemPressedGradientMiddle { get { return Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Hover; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Bg; } }
        public override Color CheckBackground { get { return Hover; } }
        public override Color CheckSelectedBackground { get { return Hover; } }
        public override Color CheckPressedBackground { get { return Hover; } }
    }
}
