using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindowTabManager
{
    // Track events before forwarding them: a later hook can consume a modifier and
    // prevent Windows' async key state from reflecting the down event we received.
    internal sealed class ModifierState
    {
        private readonly bool[] held = new bool[256];
        public bool Alt { get { return held[0xA4] || held[0xA5] || held[0x12]; } }
        public bool Win { get { return held[0x5B] || held[0x5C]; } }
        public bool Shift { get { return held[0xA0] || held[0xA1] || held[0x10]; } }
        public bool Control { get { return held[0xA2] || held[0xA3] || held[0x11]; } }
        public void Seed(Func<int, bool> down)
        {
            Array.Clear(held, 0, held.Length);
            foreach (int key in new[] { 0xA4, 0xA5, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3 }) held[key] = down(key);
        }
        public void Observe(int key, bool down)
        {
            if (key != 0x10 && key != 0x11 && key != 0x12 && key != 0x5B && key != 0x5C && (key < 0xA0 || key > 0xA5)) return;
            held[key] = down;
            if (!down) {
                if (key == 0x12) held[0xA4] = held[0xA5] = false;
                if (key == 0x10) held[0xA0] = held[0xA1] = false;
                if (key == 0x11) held[0xA2] = held[0xA3] = false;
            }
        }
        public bool AltFor(int key, bool down, uint flags)
        {
            // LLKHF_ALTDOWN also covers starting the hook while Alt was held.
            // On Alt-up the flag describes the event context, not remaining keys.
            return Alt || ((down || (key != 0x12 && key != 0xA4 && key != 0xA5)) && (flags & 0x20) != 0);
        }
    }
    internal enum SwitchAction { None, BeginAlt, StepForward, StepBackward, CommitAlt, Cancel, Overview }
    internal sealed class KeyDecision
    {
        public bool Swallow, MaskMenu;
        public SwitchAction Action;
    }
    internal sealed class ShortcutState
    {
        public bool AltSession, OverviewOpen;
        public int Generation;
        private bool tabConsumed, escapeConsumed;
        public void Reset() { AltSession = OverviewOpen = tabConsumed = escapeConsumed = false; }
        public void ClosePanel(int expectedGeneration = 0) { if (expectedGeneration == 0 || expectedGeneration == Generation) AltSession = OverviewOpen = false; }
        public KeyDecision Handle(int key, bool down, bool alt, bool win, bool shift, bool control)
        {
            var d = new KeyDecision();
            if (key == 9 && !down && tabConsumed) { tabConsumed = false; d.Swallow = true; return d; }
            if (key == 27 && !down && escapeConsumed) { escapeConsumed = false; d.Swallow = true; return d; }
            if ((key == 0xA4 || key == 0xA5 || key == 0x12) && !down && !alt && AltSession) {
                AltSession = false; d.Action = SwitchAction.CommitAlt; return d;
            }
            if (!down) return d;
            if (key == 27 && (AltSession || OverviewOpen)) {
                AltSession = OverviewOpen = false; escapeConsumed = true;
                d.Swallow = true; d.Action = SwitchAction.Cancel; return d;
            }
            if (key != 9 || control) return d;
            if (alt && !win) {
                d.Swallow = tabConsumed = true;
                if (!AltSession) { Generation++; AltSession = true; OverviewOpen = false; d.MaskMenu = true; d.Action = SwitchAction.BeginAlt; }
                else d.Action = shift ? SwitchAction.StepBackward : SwitchAction.StepForward;
            } else if (win && !alt) {
                d.Swallow = tabConsumed = true;
                if (!OverviewOpen) { Generation++; OverviewOpen = true; AltSession = false; d.MaskMenu = true; d.Action = SwitchAction.Overview; }
            }
            return d;
        }
    }
    internal sealed class SwitchCommand
    {
        public SwitchAction Action;
        public Native.MonitorInfo Monitor;
        public IntPtr Foreground;
        public bool Reverse;
        public int Generation;
    }
    internal sealed class KeyboardHook : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Action<SwitchCommand> publish;
        private readonly Action<string> report;
        private Control dispatcher;
        private IntPtr hook;
        private Native.KeyboardProc callback;
        private readonly ShortcutState state = new ShortcutState();
        private readonly ModifierState modifiers = new ModifierState();
        private Exception startError;
        private static readonly UIntPtr Tag = new UIntPtr(0x57544D);
        public volatile bool UseActiveMonitor;
        public KeyboardHook(Action<SwitchCommand> publish, Action<string> report)
        {
            this.publish = publish; this.report = report;
            thread = new Thread(Run) { IsBackground = true, Name = "Keyboard hook" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.WaitOne();
            if (startError != null) { thread.Join(); ready.Dispose(); throw startError; }
        }
        private void Run()
        {
            using (dispatcher = new Control()) {
                var hwnd = dispatcher.Handle;
                modifiers.Seed(Down);
                callback = OnKey;
                hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) startError = new Win32Exception(Marshal.GetLastWin32Error(), "無法安裝鍵盤攔截。");
                ready.Set();
                if (startError != null) return;
                try { Application.Run(); }
                finally { if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            }
        }
        private static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }
        private IntPtr OnKey(int code, IntPtr wp, IntPtr lp)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, wp, lp);
            try {
                var data = (Native.KeyboardData)Marshal.PtrToStructure(lp, typeof(Native.KeyboardData));
                if (data.Extra == Tag || (data.Flags & 0x10) != 0) return Native.CallNextHookEx(hook, code, wp, lp);
                int key = (int)data.Key;
                bool down = wp.ToInt64() == 0x100 || wp.ToInt64() == 0x104;
                modifiers.Observe(key, down);
                bool reverse = modifiers.Shift;
                var d = state.Handle(key, down, modifiers.AltFor(key, down, data.Flags), modifiers.Win, reverse, modifiers.Control);
                if (d.MaskMenu) {
                    // The modifier down already reached Windows. A neutral Ctrl pair marks the chord
                    // as used, preventing a bare Win/Alt release from opening Start/app menus.
                    var inputs = new Native.Input[2];
                    for (int i = 0; i < 2; i++) { inputs[i].Type = 1; inputs[i].Data.Keyboard = new Native.KeyInput { Key = 0x11, Flags = (uint)(i * 2), Extra = Tag }; }
                    if (Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.Input))) != 2)
                        report("Windows 拒絕快捷鍵選單抑制；放鍵時可能出現原生選單。");
                }
                if (d.Action != SwitchAction.None) {
                    var command = new SwitchCommand { Action = d.Action, Reverse = reverse, Generation = state.Generation };
                    if (d.Action == SwitchAction.BeginAlt || d.Action == SwitchAction.Overview) {
                        command.Foreground = Native.GetForegroundWindow();
                        command.Monitor = Native.TargetMonitor(UseActiveMonitor);
                    }
                    publish(command);
                }
                if (d.Swallow) return new IntPtr(1);
            } catch (Exception e) {
                state.Reset(); report("快捷鍵處理失敗，已取消這次操作：" + e.Message);
                publish(new SwitchCommand { Action = SwitchAction.Cancel });
            }
            return Native.CallNextHookEx(hook, code, wp, lp);
        }
        public void ClosePanel(int expectedGeneration = 0)
        {
            if (dispatcher != null && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)delegate { state.ClosePanel(expectedGeneration); });
        }
        public void ResyncModifiers()
        {
            if (dispatcher != null && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)delegate { state.Reset(); modifiers.Seed(Down); });
        }
        public void Dispose()
        {
            if (dispatcher != null && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)delegate { state.Reset(); Application.ExitThread(); });
            thread.Join(2000); ready.Dispose();
        }
    }
}
