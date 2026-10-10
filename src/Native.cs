using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WindowTabManager
{
    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        internal delegate IntPtr KeyboardProc(int code, IntPtr wparam, IntPtr lparam);
        internal delegate void EventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
        { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit, Size = 32)] internal struct InputUnion { [FieldOffset(0)] public KeyInput Keyboard; }
        [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, KeyboardProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] internal static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string module);
        [DllImport("kernel32.dll")] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int length);
        [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int type, out int value, int size, out int length);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        internal static MonitorInfo Monitor(IntPtr handle)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            GetMonitorInfo(handle, ref info); return info;
        }
        internal static string Elevation(uint pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid), token = IntPtr.Zero;
            if (process == IntPtr.Zero) return "無法讀取";
            try {
                int value, length;
                if (!OpenProcessToken(process, 8, out token) || !GetTokenInformation(token, 20, out value, 4, out length)) return "無法讀取";
                return value != 0 ? "系統管理員" : "一般權限";
            } finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
        }
        internal static string MonitorOf(IntPtr hwnd) { return Monitor(MonitorFromWindow(hwnd, 2)).Device ?? "未知螢幕"; }
        internal static MonitorInfo TargetMonitor(bool active)
        {
            Point p; GetCursorPos(out p);
            return Monitor(active ? MonitorFromWindow(GetForegroundWindow(), 2) : MonitorFromPoint(p, 2));
        }
        internal static string ProcessPath(uint pid, out long creation)
        {
            creation = 0;
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return "";
            try {
                long exit, kernel, user; GetProcessTimes(process, out creation, out exit, out kernel, out user);
                var path = new StringBuilder(32768); int length = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : "";
            } finally { CloseHandle(process); }
        }
        internal static bool StillSame(WindowInfo w)
        {
            uint pid; GetWindowThreadProcessId(w.Handle, out pid);
            if (!IsWindow(w.Handle) || pid != w.ProcessId) return false;
            long created; ProcessPath(pid, out created);
            var cls = new StringBuilder(256); GetClassName(w.Handle, cls, cls.Capacity);
            return w.Key == w.Handle.ToInt64() + ":" + pid + ":" + created + ":" + cls;
        }
        internal static bool Activate(WindowInfo w)
        {
            if (!StillSame(w)) return false;
            if (IsIconic(w.Handle)) ShowWindowAsync(w.Handle, 9);
            return SetForegroundWindow(w.Handle);
        }
        internal static Icon ReadIcon(IntPtr hwnd)
        {
            IntPtr icon;
            SendMessageTimeout(hwnd, 0x7F, new IntPtr(2), IntPtr.Zero, 2, 25, out icon);
            if (icon == IntPtr.Zero) SendMessageTimeout(hwnd, 0x7F, new IntPtr(0), IntPtr.Zero, 2, 25, out icon);
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, -34);
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, -14);
            try { return icon == IntPtr.Zero ? (Icon)SystemIcons.Application.Clone() : (Icon)Icon.FromHandle(icon).Clone(); }
            catch (ArgumentException) { return (Icon)SystemIcons.Application.Clone(); }
        }
        internal static WindowInfo[] Enumerate(Dictionary<IntPtr, long> mru, bool includeOwn)
        {
            var windows = new List<WindowInfo>();
            uint own = (uint)Process.GetCurrentProcess().Id;
            var paths = new Dictionary<uint, Tuple<string, long>>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr data) {
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                if ((!includeOwn && pid == own) || !IsWindowVisible(hwnd) || hwnd == IntPtr.Zero) return true;
                long style = GetWindowLongPtr(hwnd, -20).ToInt64();
                if ((style & 0x80) != 0 || (style & 0x08000000) != 0) return true;
                if (GetWindow(hwnd, 4) != IntPtr.Zero && (style & 0x40000) == 0) return true;
                int cloaked;
                if (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                var title = new StringBuilder(1024); GetWindowText(hwnd, title, title.Capacity);
                if (title.Length == 0) return true;
                var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() == "Progman" || cls.ToString() == "WorkerW" || cls.ToString() == "Shell_TrayWnd" || cls.ToString() == "Shell_SecondaryTrayWnd") return true;
                Tuple<string, long> process;
                if (!paths.TryGetValue(pid, out process)) {
                    long created; string path = ProcessPath(pid, out created);
                    process = Tuple.Create(path, created); paths[pid] = process;
                }
                long used; mru.TryGetValue(hwnd, out used);
                windows.Add(new WindowInfo {
                    Handle = hwnd, ProcessId = pid, Executable = process.Item1, ClassName = cls.ToString(), Title = title.ToString(),
                    Key = hwnd.ToInt64() + ":" + pid + ":" + process.Item2 + ":" + cls,
                    AppName = String.IsNullOrEmpty(process.Item1) ? "受限程式 (" + pid + ")" : System.IO.Path.GetFileNameWithoutExtension(process.Item1),
                    Monitor = MonitorOf(hwnd), Minimized = IsIconic(hwnd), LastUsed = used
                });
                return true;
            }, IntPtr.Zero);
            return windows.OrderByDescending(w => w.LastUsed).ToArray();
        }
    }

    internal sealed class WindowInventory : IDisposable
    {
        private Thread thread;
        private Control dispatcher;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Action<WindowInfo[]> publish;
        private readonly Action<string> report;
        private readonly Dictionary<IntPtr, long> mru = new Dictionary<IntPtr, long>();
        private Native.EventProc callback;
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private bool dirty = true;
        private long sequence;
        private int idleTicks;
        public WindowInventory(Action<WindowInfo[]> publish, Action<string> report)
        {
            this.publish = publish; this.report = report;
            thread = new Thread(Run) { IsBackground = true, Name = "Window inventory" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.WaitOne();
        }
        private void Run()
        {
            using (dispatcher = new Control()) {
                var handle = dispatcher.Handle;
                callback = OnEvent;
                hooks.Add(Native.SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2));
                hooks.Add(Native.SetWinEventHook(0x8000, 0x8003, IntPtr.Zero, callback, 0, 0, 2));
                hooks.Add(Native.SetWinEventHook(0x800B, 0x800C, IntPtr.Zero, callback, 0, 0, 2));
                hooks.Add(Native.SetWinEventHook(0x16, 0x17, IntPtr.Zero, callback, 0, 0, 2));
                mru[Native.GetForegroundWindow()] = ++sequence;
                using (var timer = new System.Windows.Forms.Timer { Interval = 300 }) {
                    timer.Tick += delegate { if (dirty || ++idleTicks >= 17) Refresh(); };
                    timer.Start(); ready.Set();
                    if (hooks.Any(h => h == IntPtr.Zero)) report("部分視窗事件註冊失敗，使用 5 秒補查。");
                    Refresh(); Application.Run();
                }
                foreach (var hook in hooks) if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
            }
        }
        private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint tid, uint time)
        {
            if (hwnd == IntPtr.Zero || obj != 0 || child != 0) return;
            if (evt == 3) mru[hwnd] = ++sequence;
            if (evt == 0x8001) mru.Remove(hwnd);
            dirty = true;
        }
        private void Refresh()
        {
            dirty = false; idleTicks = 0;
            try {
                var snapshot = Native.Enumerate(mru, false);
                var handles = new HashSet<IntPtr>(snapshot.Select(w => w.Handle));
                foreach (var hwnd in mru.Keys.Where(h => !handles.Contains(h)).ToArray()) mru.Remove(hwnd);
                publish(snapshot);
            } catch (Exception e) { report("視窗清單更新失敗：" + e.Message); }
        }
        public void RequestRefresh() { if (dispatcher != null && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)delegate { dirty = true; }); }
        public void Dispose()
        {
            if (dispatcher != null && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)Application.ExitThread);
            thread.Join(2000); ready.Dispose();
        }
    }
}
