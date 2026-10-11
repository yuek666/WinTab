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

}
