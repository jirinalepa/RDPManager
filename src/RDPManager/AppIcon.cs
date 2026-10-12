using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using RDPManager.Docking;

namespace RDPManager;

/// <summary>Draws the application icon (a monitor on an accent tile) so no binary asset is needed.</summary>
public static class AppIcon
{
    private static Icon? _icon;
    private static System.Windows.Media.ImageSource? _imageSource;

    public static Icon Icon => _icon ??= Create(32);

    public static System.Windows.Media.ImageSource ImageSource => _imageSource ??= Imaging.CreateBitmapSourceFromHIcon(
        Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private static Icon Create(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var s = size / 32f;
            using var tile = RoundedRect(new RectangleF(1 * s, 1 * s, 30 * s, 30 * s), 7 * s);
            using var tileBrush = new SolidBrush(Color.FromArgb(0, 103, 192));
            g.FillPath(tileBrush, tile);

            using var white = new SolidBrush(Color.White);
            using var screen = RoundedRect(new RectangleF(6 * s, 8 * s, 20 * s, 13 * s), 2 * s);
            g.FillPath(white, screen);
            using var inner = new SolidBrush(Color.FromArgb(0, 70, 140));
            g.FillRectangle(inner, 8 * s, 10 * s, 16 * s, 9 * s);
            g.FillRectangle(white, 14.5f * s, 21 * s, 3 * s, 3 * s);
            g.FillRectangle(white, 11 * s, 24 * s, 10 * s, 2 * s);
        }
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
