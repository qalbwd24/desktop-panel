using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopPanel {
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWallpaper {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string path);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
    internal static class Wallpaper {
        internal static string Build(Rectangle screen, string directory) {
            Directory.CreateDirectory(directory);
            string imagePath = null; int position = 4; Color background = Color.FromArgb(25, 28, 33);
            object instance = null;
            try {
                instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
                var desktop = (IDesktopWallpaper)instance; position = desktop.GetPosition();
                uint rgb = desktop.GetBackgroundColor(); background = Color.FromArgb((int)(rgb & 255), (int)((rgb >> 8) & 255), (int)((rgb >> 16) & 255));
                uint count = desktop.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++) {
                    string monitor = desktop.GetMonitorDevicePathAt(i); RECT rect = desktop.GetMonitorRECT(monitor);
                    if (rect.Left == screen.Left && rect.Top == screen.Top && rect.Right == screen.Right && rect.Bottom == screen.Bottom) { imagePath = desktop.GetWallpaper(monitor); break; }
                }
            } catch (Exception e) { Program.Log("Wallpaper COM fallback: " + e.Message); }
            finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance); }
            if (String.IsNullOrEmpty(imagePath)) { using (var key = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop")) { if (key != null) imagePath = key.GetValue("WallPaper") as string; } }
            string output = Path.Combine(directory, "wallpaper.png");
            using (var bitmap = new Bitmap(screen.Width, screen.Height)) using (Graphics graphics = Graphics.FromImage(bitmap)) {
                graphics.Clear(background); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (!String.IsNullOrEmpty(imagePath) && File.Exists(imagePath)) using (Image image = Image.FromFile(imagePath)) {
                    if (position == 1) { using (var brush = new TextureBrush(image)) { brush.TranslateTransform(-screen.Left, -screen.Top); graphics.FillRectangle(brush, 0, 0, screen.Width, screen.Height); } }
                    else {
                        Rectangle target = position == 5 ? System.Windows.Forms.SystemInformation.VirtualScreen : screen;
                        double factor = position == 0 ? 1 : position == 3 ? Math.Min((double)target.Width / image.Width, (double)target.Height / image.Height) : Math.Max((double)target.Width / image.Width, (double)target.Height / image.Height);
                        float width = position == 2 ? target.Width : (float)(image.Width * factor), height = position == 2 ? target.Height : (float)(image.Height * factor);
                        float left = (target.Width - width) / 2 + target.Left - screen.Left, top = (target.Height - height) / 2 + target.Top - screen.Top;
                        graphics.DrawImage(image, left, top, width, height);
                    }
                }
                string temp = output + ".tmp"; bitmap.Save(temp, ImageFormat.Png); if (File.Exists(output)) File.Delete(output); File.Move(temp, output);
            }
            Program.Log("Wallpaper cached for " + screen.ToString());
            return "https://cache.desk.local/wallpaper.png?v=" + DateTime.UtcNow.Ticks;
        }
    }
}
