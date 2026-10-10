using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WindowTabManager
{
    internal static class AppIcons
    {
        // Two shared icons live for the lifetime of the process; callers must not dispose them.
        public static readonly Icon Window = Load(SystemInformation.IconSize);
        public static readonly Icon Tray = Load(SystemInformation.SmallIconSize);

        private static Icon Load(Size size)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WindowTabManager.AppIcon.ico"))
            using (var source = new Icon(stream, size)) return (Icon)source.Clone();
        }
    }
}
