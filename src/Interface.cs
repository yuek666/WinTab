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
            var b = new Button { Text = text, AutoSize = true, Height = 32, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(3), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(216, 222, 231); b.FlatAppearance.MouseOverBackColor = Color.FromArgb(237, 244, 252);
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

}
