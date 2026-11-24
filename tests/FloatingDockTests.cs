using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SalaryStopwatch;

class FloatingDockTests
{
    static int assertions;
    static void Check(bool condition, string name)
    { assertions++; if (!condition) throw new Exception("FAIL: " + name); }
    static void Near(double actual, double expected, string name)
    { Check(Math.Abs(actual - expected) < 0.01, name + ": " + actual); }
    static void Invoke(Controller controller, string method, params object[] arguments)
    { typeof(Controller).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments); }
    static void Field(Controller controller, string name, object value)
    { typeof(Controller).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value); }

    [STAThread]
    static void Main()
    {
        Application application = new Application();
        Rect area = new Rect(-1920, -100, 1920, 1080);
        Check(FloatingDock.FindEdge(area, new Rect(-1910, 100, FloatingDock.BallWidth, FloatingDock.BallHeight)) == -1, "snap to left on secondary monitor");
        Check(FloatingDock.FindEdge(area, new Rect(-145, 100, FloatingDock.BallWidth, FloatingDock.BallHeight)) == 1, "snap to right on secondary monitor");
        Check(FloatingDock.FindEdge(area, new Rect(-900, 100, FloatingDock.BallWidth, FloatingDock.BallHeight)) == 0, "dragging away undocks");
        foreach (int edge in new[] { -1, 1 })
        {
            Rect shown = FloatingDock.Bounds(area, 200, edge, false);
            Rect hidden = FloatingDock.Bounds(area, 200, edge, true);
            Near(hidden.Width, 12, "hidden tab width");
            Near(shown.Width, 208 * 2.0 / 3, "shown scaled width");
            Near(shown.Top, hidden.Top, "stable vertical position");
            Near(edge < 0 ? hidden.Left : hidden.Right, edge < 0 ? area.Left : area.Right, "tab stays inside monitor edge");
            Check(area.Contains(shown) && area.Contains(hidden), "ball and tab remain on monitor");
        }
        Near(FloatingDock.Bounds(area, -999, -1, false).Top, area.Top, "top boundary");
        Near(FloatingDock.Bounds(area, 9999, 1, false).Bottom, area.Bottom, "bottom boundary");
        foreach (Rect secondary in new[] {
            new Rect(1920, 0, 2560, 1400), new Rect(-2560, -100, 2560, 1400),
            new Rect(0, -1440, 2560, 1440), new Rect(0, 1080, 2560, 1440) })
        {
            Rect destination = new Rect(secondary.Left + 300, secondary.Top + 200, FloatingDock.BallWidth, FloatingDock.BallHeight);
            Check(FloatingDock.ConstrainToArea(destination, secondary) == destination, "drop stays at destination on any secondary monitor");
            Rect crossing = new Rect(secondary.Left - 30, secondary.Top + 100, FloatingDock.BallWidth, FloatingDock.BallHeight);
            Rect dropped = FloatingDock.ConstrainToArea(crossing, secondary);
            Near(dropped.Left, secondary.Left - 4, "straddling drop is constrained to destination monitor");
            Near(dropped.Top, crossing.Top, "cross-screen drop keeps vertical position");
            Rect hidden = FloatingDock.Bounds(secondary, destination.Top, 1, true);
            Near(hidden.Right, secondary.Right, "secondary docking uses secondary edge");
            Near(FloatingDock.Bounds(secondary, hidden.Top, 1, false).Right, secondary.Right, "secondary reveal stays on same monitor");
        }

        // Exercise the native movement path on a never-shown window, without moving the cursor.
        Window probe = new Window { Width = FloatingDock.BallWidth, Height = FloatingDock.BallHeight, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        new WindowInteropHelper(probe).EnsureHandle();
        HwndSource probeSource = HwndSource.FromHwnd(new WindowInteropHelper(probe).Handle);
        Matrix fromDevice = probeSource.CompositionTarget.TransformFromDevice;
        foreach (Point point in new[] { new Point(2400, 200), new Point(-1600, 250), new Point(600, -900), new Point(800, 1600) })
        {
            Check(FloatingDock.MoveNative(probe, point), "native movement accepts cross-screen desktop coordinates");
            Rect actual = FloatingDock.WindowBounds(probe);
            Point expected = fromDevice.Transform(point);
            Near(actual.Left, expected.X, "native movement preserves horizontal drop position");
            Near(actual.Top, expected.Y, "native movement preserves vertical drop position");
        }
        probe.Close();

        Controller controller = new Controller();
        Window window = controller.Window;
        Invoke(controller, "SetCompact", true);
        Near(window.Width, FloatingDock.BallWidth, "controller uses 2/3 width");
        Near(window.Height, FloatingDock.BallHeight, "controller uses 2/3 height");
        Field(controller, "dockArea", area);
        foreach (int edge in new[] { -1, 1 })
        {
            Field(controller, "dockEdge", edge);
            Invoke(controller, "SetDockHidden", true);
            Check(((Border)window.FindName("DockTab")).Visibility == Visibility.Visible, "tab is shown when hidden");
            Check(((Viewbox)window.FindName("Viewport")).Visibility == Visibility.Collapsed, "income content hidden while docked");
            Near(window.Width, 12, "controller hides to narrow tab");
            Invoke(controller, "SetDockHidden", false);
            Near(window.Width, FloatingDock.BallWidth, "hover restores mini size");
            Check(((Viewbox)window.FindName("Viewport")).Visibility == Visibility.Visible, "hover restores content");
        }
        Invoke(controller, "SetCompact", false);
        Near(window.Width, 412, "expanded panel width unchanged");
        Near(window.Height, 606, "expanded panel height unchanged");
        Check(((Border)window.FindName("DockTab")).Visibility == Visibility.Collapsed, "panel never shows edge tab");
        Invoke(controller, "SetCompact", true);
        ((TextBlock)window.FindName("BallEarned")).Text = "¥ 192.00";
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Resources = window.Resources;
        window.Content = null;
        Size size = new Size(FloatingDock.BallWidth, FloatingDock.BallHeight);
        root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
        FrameworkElement ball = (FrameworkElement)window.FindName("BallShell");
        Rect rendered = ball.TransformToAncestor(root).TransformBounds(new Rect(0, 0, ball.ActualWidth, ball.ActualHeight));
        Check(Math.Abs(rendered.Width - 184 * FloatingDock.Scale) < 1, "whole ball scales to 2/3");
        Check(Math.Abs(rendered.Height - 70 * FloatingDock.Scale) < 1, "ball height scales to 2/3");
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (Stream file = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mini-ball.png"))) encoder.Save(file);
        Console.WriteLine("PASS: " + assertions + " docking and layout assertions (no windows opened).");
    }
}
