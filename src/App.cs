using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Markup;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace SalaryStopwatch
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "SalaryStopwatch.Desktop.v1", out created))
            {
                if (!created) { MessageBox.Show("薪资秒表已经在运行，请在桌面上找到悬浮球。", "薪资秒表"); return; }
                try
                {
                    Application app = new Application();
                    Controller controller = new Controller();
                    app.Run(controller.Window);
                }
                catch (Exception exception)
                { MessageBox.Show("秒表暂时无法启动：\n" + exception.Message, "薪资秒表"); }
            }
        }
    }

    public sealed class Controller
    {
        public Window Window;
        private Settings settings;
        private DispatcherTimer timer, dockTimer;
        private bool settingsOpen, ready, dragged, dragging, dockHidden;
        private int dockEdge;
        private Rect dockArea;
        private Point pointerDown, dragOrigin;
        private UIElement dragSurface;
        private bool dragBall;
        private readonly string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "settings.json");
        private readonly Brush green = new SolidColorBrush(Color.FromRgb(0x65, 0x77, 0x46));
        private readonly Brush muted = new SolidColorBrush(Color.FromRgb(0x94, 0x9B, 0x89));

        private T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        private void Text(string name, string text) { Find<TextBlock>(name).Text = text; }
        private void Click(string name, RoutedEventHandler handler) { Find<Button>(name).Click += handler; }

        public Controller()
        {
            using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream("SalaryStopwatch.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            settings = Load();
            dockEdge = settings.DockEdge == -1 || settings.DockEdge == 1 ? settings.DockEdge : 0;
            dockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            dockTimer.Tick += delegate
            {
                dockTimer.Stop();
                if (settings.Compact && dockEdge != 0 && !dragging && !Window.IsMouseOver && !Find<Border>("BallShell").ContextMenu.IsOpen)
                    SetDockHidden(true);
            };
            Window.MouseEnter += delegate { dockTimer.Stop(); if (dockHidden) SetDockHidden(false); };
            Window.MouseLeave += delegate { ScheduleDock(); };
            Window.Topmost = settings.Pinned;
            Click("CloseButton", delegate { Window.Close(); });
            Click("MinimizeButton", delegate { SetCompact(true); });
            Click("FloatButton", delegate { SetCompact(true); });
            Click("PrivacyButton", delegate { settings.HideAmounts = !settings.HideAmounts; Persist(); Refresh(); });
            Click("DashboardTab", delegate { ShowPage(false); });
            Click("SettingsTab", delegate { ShowPage(true); });
            Click("SaveButton", delegate { SaveForm(); });
            Click("PinButton", delegate { TogglePin(); });
            Find<CheckBox>("LunchCheck").Checked += delegate { SetLunchInputs(); };
            Find<CheckBox>("LunchCheck").Unchecked += delegate { SetLunchInputs(); };
            Grid header = Find<Grid>("DragHeader");
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                DependencyObject target = args.OriginalSource as DependencyObject;
                while (target != null && !(target is Button)) target = VisualTreeHelper.GetParent(target);
                if (target is Button) return;
                BeginDrag(header, args, false);
            };
            Border ball = Find<Border>("BallShell");
            ball.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            { BeginDrag(ball, args, true); };
            foreach (UIElement surface in new UIElement[] { header, ball })
            {
                surface.MouseMove += DragMouseMove;
                surface.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
                { if (dragSurface != null) { EndDrag(true); args.Handled = true; } };
                surface.LostMouseCapture += delegate { if (dragSurface != null) EndDrag(false); };
            }
            ContextMenu menu = new ContextMenu();
            AddMenu(menu, "展开收入面板", delegate { SetCompact(false); ShowPage(false); });
            AddMenu(menu, "工作设置", delegate { SetCompact(false); ShowPage(true); });
            AddMenu(menu, "隐藏 / 显示金额", delegate { settings.HideAmounts = !settings.HideAmounts; Persist(); Refresh(); });
            AddMenu(menu, "切换窗口置顶", delegate { TogglePin(); });
            AddMenu(menu, "取消贴边收起", delegate
            {
                dockTimer.Stop();
                if (dockHidden) SetDockHidden(false);
                dockEdge = 0; settings.DockEdge = 0; SavePosition();
            });
            menu.Items.Add(new Separator());
            AddMenu(menu, "退出秒表", delegate { Window.Close(); });
            ball.ContextMenu = menu;
            menu.Opened += delegate { dockTimer.Stop(); };
            menu.Closed += delegate { ScheduleDock(); };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { SetCompact(!settings.Compact); args.Handled = true; }
                if (args.Key == Key.Enter && settingsOpen) { SaveForm(); args.Handled = true; }
            };
            Window.Loaded += delegate
            {
                SetCompact(settings.Compact);
                ready = true;
                Rect area = SystemParameters.WorkArea;
                Window.Left = !double.IsNaN(settings.Left) && !double.IsInfinity(settings.Left) && settings.Left != -1 ? settings.Left : area.Right - Window.Width - 30;
                Window.Top = !double.IsNaN(settings.Top) && !double.IsInfinity(settings.Top) && settings.Top != -1 ? settings.Top : area.Top + 70;
                ClampPosition(); Refresh();
                if (settings.Compact && dockEdge != 0)
                {
                    dockArea = FloatingDock.WorkArea(Window);
                    SetDockHidden(false); ScheduleDock();
                }
            };
            Window.Closing += delegate { if (timer != null) timer.Stop(); dockTimer.Stop(); SavePosition(); };
            FillForm();
            ShowPage(false);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
        }

        private void AddMenu(ContextMenu menu, string label, Action action)
        { MenuItem item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); }

        private Settings Load()
        {
            if (!File.Exists(dataPath)) return new Settings();
            try
            {
                using (Stream file = File.OpenRead(dataPath))
                {
                    Settings saved = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(file);
                    if (saved != null && SalaryEngine.Validate(saved) == null && !double.IsNaN(saved.Left) && !double.IsNaN(saved.Top)) return saved;
                }
            }
            catch { }
            MessageBox.Show("保存的设置无法读取，将使用默认设置。原文件会保留为 settings.recovery.json。", "薪资秒表");
            try { File.Copy(dataPath, Path.Combine(Path.GetDirectoryName(dataPath), "settings.recovery.json"), true); } catch { }
            return new Settings();
        }

        private bool Persist()
        {
            string temporary = dataPath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
                using (Stream file = File.Create(temporary)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(file, settings);
                if (File.Exists(dataPath)) File.Replace(temporary, dataPath, null); else File.Move(temporary, dataPath);
                return true;
            }
            catch (Exception error)
            { MessageBox.Show("本次设置可继续使用，但未能保存到本机。\n请将程序放到可写入的文件夹。\n\n" + error.Message, "薪资秒表"); return false; }
        }

        private void SavePosition()
        {
            if (!ready) return;
            settings.Left = dockHidden ? FloatingDock.Bounds(dockArea, Window.Top, dockEdge, false).Left : Window.Left;
            settings.Top = Window.Top; settings.DockEdge = dockEdge; Persist();
        }

        private void ClampPosition()
        {
            ClampPosition(FloatingDock.WorkArea(Window));
        }

        private void ClampPosition(Rect area)
        {
            Rect bounds = FloatingDock.ConstrainToArea(new Rect(Window.Left, Window.Top, Window.Width, Window.Height), area);
            Window.Left = bounds.Left; Window.Top = bounds.Top;
        }

        private void BeginDrag(UIElement surface, MouseButtonEventArgs args, bool ball)
        {
            if (!FloatingDock.DragOrigin(Window, out pointerDown, out dragOrigin)) return;
            dockTimer.Stop(); dragged = false; dragging = false; dragBall = ball;
            dragSurface = surface;
            if (!surface.CaptureMouse()) dragSurface = null;
            args.Handled = true;
        }

        private void DragMouseMove(object sender, MouseEventArgs args)
        {
            if (dragSurface == null || !dragSurface.IsMouseCaptured) return;
            if (args.LeftButton != MouseButtonState.Pressed) { EndDrag(false); return; }
            Point pointer;
            if (!FloatingDock.CursorPosition(out pointer)) return;
            Vector delta = pointer - pointerDown;
            if (!dragged && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5) return;
            dragged = true; dragging = true; dockEdge = 0; settings.DockEdge = 0;
            dockTimer.Stop();
            FloatingDock.MoveNative(Window, dragOrigin + delta);
            args.Handled = true;
        }

        private void EndDrag(bool allowClick)
        {
            UIElement surface = dragSurface;
            bool moved = dragged;
            bool openPanel = dragBall;
            dragSurface = null; dragging = false;
            if (surface != null && surface.IsMouseCaptured) surface.ReleaseMouseCapture();
            if (moved)
            {
                if (settings.Compact) SnapAfterDrag();
                else
                {
                    Rect area = FloatingDock.WorkAreaAtCursor(Window);
                    Rect bounds = FloatingDock.WindowBounds(Window);
                    Window.Left = bounds.Left; Window.Top = bounds.Top;
                    ClampPosition(area); SavePosition();
                }
            }
            else if (allowClick && openPanel) SetCompact(false);
            else ScheduleDock();
        }

        private void SetCompact(bool compact)
        {
            dockTimer.Stop();
            double right = Window.Left + Window.Width;
            Rect area = ready ? FloatingDock.WorkArea(Window) : SystemParameters.WorkArea;
            dockHidden = false;
            Find<Border>("DockTab").Visibility = Visibility.Collapsed;
            Find<Viewbox>("Viewport").Visibility = Visibility.Visible;
            settings.Compact = compact;
            Find<Border>("PanelShell").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            Find<Border>("BallShell").Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            Find<Grid>("DesignRoot").Width = compact ? 208 : 412;
            Find<Grid>("DesignRoot").Height = compact ? 94 : 606;
            Window.Width = compact ? FloatingDock.BallWidth : 412;
            Window.Height = compact ? FloatingDock.BallHeight : 606;
            if (ready)
            {
                Window.Left = dockEdge < 0 ? area.Left : dockEdge > 0 ? area.Right - Window.Width : right - Window.Width;
                ClampPosition(area);
                if (compact && dockEdge != 0) { dockArea = area; ScheduleDock(); }
                SavePosition();
            }
            Refresh();
        }

        private void ScheduleDock()
        {
            if (!ready || !settings.Compact || dockEdge == 0 || dragging || dockHidden) return;
            dockTimer.Stop(); dockTimer.Start();
        }

        private void SnapAfterDrag()
        {
            // The cursor selects the drop monitor even while the ball straddles two screens.
            // Read the native position rather than cached WPF Left/Top after moving.
            dockArea = FloatingDock.WorkAreaAtCursor(Window);
            Rect bounds = FloatingDock.WindowBounds(Window);
            Window.Left = bounds.Left; Window.Top = bounds.Top;
            dockEdge = FloatingDock.FindEdge(dockArea, bounds);
            if (dockEdge != 0) { SetDockHidden(false); ScheduleDock(); }
            else ClampPosition(dockArea);
            SavePosition();
        }

        private void SetDockHidden(bool hidden)
        {
            if (!settings.Compact || dockEdge == 0) return;
            dockHidden = hidden;
            Rect bounds = FloatingDock.Bounds(dockArea, Window.Top, dockEdge, hidden);
            Find<Viewbox>("Viewport").Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            Border tab = Find<Border>("DockTab");
            tab.CornerRadius = dockEdge < 0 ? new CornerRadius(0, 8, 8, 0) : new CornerRadius(8, 0, 0, 8);
            tab.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
            Window.Left = bounds.Left; Window.Top = bounds.Top;
            Window.Width = bounds.Width; Window.Height = bounds.Height;
        }

        private void TogglePin()
        { settings.Pinned = !settings.Pinned; Window.Topmost = settings.Pinned; Persist(); Refresh(); }

        private void ShowPage(bool showSettings)
        {
            settingsOpen = showSettings;
            Find<Grid>("Dashboard").Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
            Find<Grid>("SettingsPage").Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("DashboardTab").Background = showSettings ? Brushes.Transparent : Brushes.White;
            Find<Button>("SettingsTab").Background = showSettings ? Brushes.White : Brushes.Transparent;
            if (showSettings) FillForm();
        }

        private void FillForm()
        {
            Find<TextBox>("PayInput").Text = settings.DailyPay.ToString("0.##", CultureInfo.InvariantCulture);
            Find<TextBox>("StartInput").Text = settings.Start;
            Find<TextBox>("EndInput").Text = settings.End;
            Find<TextBox>("LunchStartInput").Text = settings.LunchStart;
            Find<TextBox>("LunchEndInput").Text = settings.LunchEnd;
            Find<CheckBox>("LunchCheck").IsChecked = settings.LunchEnabled;
            for (int day = 0; day < 7; day++) Find<ToggleButton>("Day" + day).IsChecked = Array.IndexOf(settings.Workdays, day) >= 0;
            Text("ValidationText", "秒薪 = 日薪 ÷ 有效工作秒数，支持跨夜班次。");
            Find<TextBlock>("ValidationText").Foreground = muted;
            SetLunchInputs();
        }

        private void SetLunchInputs()
        {
            bool enabled = Find<CheckBox>("LunchCheck").IsChecked == true;
            Find<TextBox>("LunchStartInput").IsEnabled = enabled;
            Find<TextBox>("LunchEndInput").IsEnabled = enabled;
        }

        private void SaveForm()
        {
            decimal pay;
            bool validPay = decimal.TryParse(Find<TextBox>("PayInput").Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out pay);
            List<int> workdays = new List<int>();
            for (int day = 0; day < 7; day++) if (Find<ToggleButton>("Day" + day).IsChecked == true) workdays.Add(day);
            Settings next = new Settings { DailyPay = validPay ? pay : 0,
                Start = Find<TextBox>("StartInput").Text.Trim(), End = Find<TextBox>("EndInput").Text.Trim(),
                LunchEnabled = Find<CheckBox>("LunchCheck").IsChecked == true,
                LunchStart = Find<TextBox>("LunchStartInput").Text.Trim(), LunchEnd = Find<TextBox>("LunchEndInput").Text.Trim(), Workdays = workdays.ToArray() };
            string error = SalaryEngine.Validate(next);
            if (error != null) { Text("ValidationText", error); Find<TextBlock>("ValidationText").Foreground = Brushes.IndianRed; return; }
            settings.DailyPay = next.DailyPay; settings.Start = NormalizeTime(next.Start); settings.End = NormalizeTime(next.End);
            settings.LunchEnabled = next.LunchEnabled;
            settings.LunchStart = next.LunchEnabled ? NormalizeTime(next.LunchStart) : settings.LunchStart;
            settings.LunchEnd = next.LunchEnabled ? NormalizeTime(next.LunchEnd) : settings.LunchEnd;
            settings.Workdays = next.Workdays;
            if (Persist()) ShowPage(false);
            Refresh();
        }

        private static string NormalizeTime(string value)
        { TimeSpan time; SalaryEngine.TryTime(value, out time); return string.Format("{0:00}:{1:00}", time.Hours, time.Minutes); }

        private void Refresh()
        {
            DateTime now = DateTime.Now;
            Snapshot snapshot = SalaryEngine.Calculate(settings, now);
            bool hidden = settings.HideAmounts;
            Text("EarnedText", hidden ? "••••" : SalaryEngine.Money(snapshot.Earned));
            Text("BallEarned", hidden ? "¥ ••••" : "¥ " + SalaryEngine.Money(snapshot.Earned));
            Text("RateText", hidden ? "¥ ••••" : "¥ " + snapshot.PerSecond.ToString("0.0000", CultureInfo.InvariantCulture));
            Text("GoalText", hidden ? "¥ ••••" : "¥ " + SalaryEngine.Money(settings.DailyPay));
            Text("StateText", snapshot.State); Text("MessageText", snapshot.Message);
            Text("BallState", snapshot.State);
            Find<Ellipse>("StatusDot").Fill = snapshot.Working ? green : muted;
            Find<Ellipse>("BallDot").Fill = snapshot.Working ? green : muted;
            Find<Button>("PinButton").Foreground = settings.Pinned ? green : muted;
            Find<Button>("PinButton").ToolTip = settings.Pinned ? "已置顶 · 点击取消置顶" : "点击保持窗口置顶";
            Text("PercentText", (snapshot.Progress * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            Find<Border>("ProgressFill").Width = Math.Max(0, (Window.Width - 76) * snapshot.Progress);
            string hours = (snapshot.Shift.PaidSeconds / 3600).ToString("0.##", CultureInfo.InvariantCulture);
            Text("ScheduleText", settings.Start + " — " + settings.End + (snapshot.Shift.End.Date > snapshot.Shift.Start.Date ? " 次日" : "") + "  ·  有效工时 " + hours + " 小时");
            Text("CountdownText", snapshot.Shift.IsWorkday ? SalaryEngine.Duration((snapshot.Shift.End - now).TotalSeconds) : "--:--:--");
            Text("ClockText", now.ToString("MM.dd  ddd  HH:mm:ss", CultureInfo.GetCultureInfo("zh-CN")));
            double progress = Math.Min(0.999999, snapshot.Progress);
            double angle = progress * Math.PI * 2;
            PathGeometry geometry = new PathGeometry();
            if (progress > 0)
            {
                PathFigure figure = new PathFigure { StartPoint = new Point(19.5, 1.5) };
                figure.Segments.Add(new ArcSegment(new Point(19.5 + 18 * Math.Sin(angle), 19.5 - 18 * Math.Cos(angle)), new Size(18, 18), 0, progress > 0.5, SweepDirection.Clockwise, true));
                geometry.Figures.Add(figure);
            }
            Find<System.Windows.Shapes.Path>("BallProgress").Data = geometry;
        }
    }
}
