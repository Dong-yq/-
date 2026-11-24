using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SalaryStopwatch
{
    public static class FloatingDock
    {
        public const double Scale = 2.0 / 3.0;
        public const double BallWidth = 208 * Scale;
        public const double BallHeight = 94 * Scale;
        public const double TabWidth = 12;
        public const double SnapDistance = 18;

        public static int FindEdge(Rect area, Rect window)
        {
            if (window.Left <= area.Left + SnapDistance) return -1;
            if (window.Right >= area.Right - SnapDistance) return 1;
            return 0;
        }

        public static Rect Bounds(Rect area, double top, int edge, bool hidden)
        {
            double width = hidden ? TabWidth : BallWidth;
            double left = edge < 0 ? area.Left : area.Right - width;
            // Keep the full ball's vertical space available while its edge tab is visible.
            double y = Math.Max(area.Top, Math.Min(top, area.Bottom - BallHeight));
            return new Rect(left, y, width, BallHeight);
        }

        public static Rect ConstrainToArea(Rect window, Rect area)
        {
            double left = Math.Max(area.Left - 4, Math.Min(window.Left, area.Right - window.Width + 4));
            double top = Math.Max(area.Top - 4, Math.Min(window.Top, area.Bottom - window.Height + 4));
            return new Rect(left, top, window.Width, window.Height);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor, Work;
            public uint Flags;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        public static Rect WorkArea(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
            return MonitorArea(window, MonitorFromWindow(handle, 2));
        }

        public static Rect WorkAreaAtCursor(Window window)
        {
            NativePoint pointer;
            return GetCursorPos(out pointer) ? MonitorArea(window, MonitorFromPoint(pointer, 2)) : WorkArea(window);
        }

        private static Rect MonitorArea(Window window, IntPtr monitor)
        {
            MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
            return FromNative(window, info.Work);
        }

        private static Rect FromNative(Window window, NativeRect rectangle)
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            Matrix transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            Point start = transform.Transform(new Point(rectangle.Left, rectangle.Top));
            Point end = transform.Transform(new Point(rectangle.Right, rectangle.Bottom));
            return new Rect(start, end);
        }

        public static bool CursorPosition(out Point point)
        {
            NativePoint pointer;
            bool success = GetCursorPos(out pointer);
            point = new Point(pointer.X, pointer.Y);
            return success;
        }

        public static bool DragOrigin(Window window, out Point pointer, out Point origin)
        {
            NativeRect rectangle;
            origin = new Point();
            if (!CursorPosition(out pointer) || !GetWindowRect(new WindowInteropHelper(window).Handle, out rectangle)) return false;
            origin = new Point(rectangle.Left, rectangle.Top);
            return true;
        }

        public static bool MoveNative(Window window, Point position)
        {
            // All drag coordinates are desktop pixels; no monitor constraints until release.
            return SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
                (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        public static Rect WindowBounds(Window window)
        {
            NativeRect rectangle;
            if (GetWindowRect(new WindowInteropHelper(window).Handle, out rectangle)) return FromNative(window, rectangle);
            return new Rect(window.Left, window.Top, window.Width, window.Height);
        }
    }
}
