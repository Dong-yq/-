using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

class InputRenderingTests
{
    [STAThread]
    static void Main(string[] args)
    {
        Application application = new Application();
        Window window;
        using (Stream file = File.OpenRead(args[0])) window = (Window)XamlReader.Load(file);
        ((FrameworkElement)window.FindName("Dashboard")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)window.FindName("SettingsPage")).Visibility = Visibility.Visible;
        string[] names = { "PayInput", "StartInput", "EndInput", "LunchStartInput", "LunchEndInput" };
        string[] values = { "300", "09:00", "18:00", "12:00", "13:00" };
        TextBox[] inputs = new TextBox[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            inputs[i] = (TextBox)window.FindName(names[i]);
            inputs[i].Text = values[i];
        }
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Resources = window.Resources;
        window.Content = null;
        root.Measure(new Size(412, 606));
        root.Arrange(new Rect(0, 0, 412, 606));
        root.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap(412, 606, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        byte[] pixels = new byte[412 * 606 * 4];
        bitmap.CopyPixels(pixels, 412 * 4, 0);
        for (int i = 0; i < inputs.Length; i++)
        {
            Point position = inputs[i].TranslatePoint(new Point(0, 0), root);
            int ink = 0;
            for (int y = (int)position.Y + 5; y < position.Y + inputs[i].ActualHeight - 5; y++)
                for (int x = (int)position.X + 8; x < position.X + inputs[i].ActualWidth - 8; x++)
                {
                    int p = (y * 412 + x) * 4;
                    if (pixels[p + 3] > 200 && pixels[p] < 140 && pixels[p + 1] < 150 && pixels[p + 2] < 140) ink++;
                }
            if (ink < 20) throw new Exception(names[i] + " text is not visible (dark pixels: " + ink + ")");
            Console.WriteLine("PASS " + names[i] + ": " + ink + " visible text pixels");
        }
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (Stream file = File.Create(args[1])) encoder.Save(file);
        Console.WriteLine("PASS: all 5 inputs render visible digits without opening a window.");
    }
}
