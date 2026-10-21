using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopPanel {
    internal static class Desktop {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr extra);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string type, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string type, string title);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr child);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool EnableWindow(IntPtr window, bool enabled);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint flags);
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X; internal int Y; }
        // wv2wall demonstrates WorkerW/Progman discovery. Here we find the host with
        // SHELLDLL_DefView and clip our child to widgets, allowing native input without global hooks.
        internal static IntPtr FindIconHost() {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr extra) {
                if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { found = window; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        internal static IntPtr Attach(IntPtr window, Rectangle screen) {
            IntPtr parent = FindIconHost();
            if (parent == IntPtr.Zero) throw new InvalidOperationException("找不到 Windows 桌面图标宿主，请从托盘使用预览窗口。");
            long style = GetWindowLongPtr(window, -16).ToInt64();
            SetWindowLongPtr(window, -16, new IntPtr((style | 0x40000000L) & ~0x80000000L)); // CHILD, remove POPUP
            SetParent(window, parent);
            if (GetParent(window) != parent) throw new InvalidOperationException("桌面嵌入失败：" + Marshal.GetLastWin32Error());
            POINT point = new POINT { X = screen.X, Y = screen.Y };
            ScreenToClient(parent, ref point);
            SetWindowPos(window, IntPtr.Zero, point.X, point.Y, screen.Width, screen.Height, 0x0010 | 0x0040 | 0x0020);
            return parent;
        }
        internal static void ShowDesktopMenu(Point position) {
            IntPtr parent = FindIconHost();
            IntPtr view = FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
            if (list == IntPtr.Zero) return;
            int coordinates = (position.X & 0xffff) | ((position.Y & 0xffff) << 16);
            PostMessage(list, 0x007B, list, new IntPtr(coordinates)); // native WM_CONTEXTMENU
        }
    }
}
