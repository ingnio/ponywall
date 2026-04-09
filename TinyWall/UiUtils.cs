using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using Microsoft.Samples;
using pylorak.Windows;
using TaskDialog = Microsoft.Samples.TaskDialog;
using TaskDialogIcon = Microsoft.Samples.TaskDialogIcon;
using MethodInvoker = System.Windows.Forms.MethodInvoker;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Helpers that touch System.Windows.Forms or System.Drawing.
    /// Lives in the WinForms UI assembly so the rest of the codebase
    /// (TinyWall.Core) stays UI-free.
    /// </summary>
    internal static class UiUtils
    {
        [SuppressUnmanagedCodeSecurity]
        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            internal static extern IntPtr WindowFromPoint(Point pt);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, IntPtr dwExtraInfo);
        }

        private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const uint MOUSEEVENTF_RIGHTUP = 0x10;

        internal static void DoMouseRightClick()
        {
            uint X = (uint)Cursor.Position.X;
            uint Y = (uint)Cursor.Position.Y;
            NativeMethods.mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, X, Y, 0, IntPtr.Zero);
        }

        internal static uint GetPidUnderCursor(int x, int y)
        {
            _ = NativeMethods.GetWindowThreadProcessId(NativeMethods.WindowFromPoint(new Point(x, y)), out uint procId);
            return procId;
        }

        internal static void SetRightToLeft(Control ctrl)
        {
            RightToLeft rtl = Application.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
            ctrl.RightToLeft = rtl;
        }

        internal static Bitmap ScaleImage(Bitmap originalImage, float scaleX, float scaleY)
        {
            int newWidth = (int)Math.Round(originalImage.Width * scaleX);
            int newHeight = (int)Math.Round(originalImage.Height * scaleY);

            var newImage = new Bitmap(originalImage, newWidth, newHeight);
            try
            {
                using (Graphics g = Graphics.FromImage(newImage))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(originalImage, 0, 0, newImage.Width, newImage.Height);
                }
                return newImage;
            }
            catch
            {
                newImage.Dispose();
                throw;
            }
        }

        internal static Bitmap ResizeImage(Bitmap originalImage, int maxWidth, int maxHeight)
        {
            int newWidth = originalImage.Width;
            int newHeight = originalImage.Height;
            double aspectRatio = (double)originalImage.Width / (double)originalImage.Height;

            if (aspectRatio <= 1 && originalImage.Width > maxWidth)
            {
                newWidth = maxWidth;
                newHeight = (int)Math.Round(newWidth / aspectRatio);
            }
            else if (aspectRatio > 1 && originalImage.Height > maxHeight)
            {
                newHeight = maxHeight;
                newWidth = (int)Math.Round(newHeight * aspectRatio);
            }

            var newImage = new Bitmap(originalImage, newWidth, newHeight);
            try
            {
                using (Graphics g = Graphics.FromImage(newImage))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(originalImage, 0, 0, newImage.Width, newImage.Height);
                }
                return newImage;
            }
            catch
            {
                newImage.Dispose();
                throw;
            }
        }

        internal static Bitmap GetIconContained(string filePath, int targetWidth, int targetHeight)
        {
            IconTools.ShellIconSize icnSize = IconTools.ShellIconSize.LargeIcon;
            if ((targetHeight == 16) && (targetWidth == 16))
                icnSize = IconTools.ShellIconSize.SmallIcon;

            using var icon = IconTools.GetIconForExtension(filePath, icnSize);
            if ((icon.Width == targetWidth) && (icon.Height == targetHeight))
            {
                return icon.ToBitmap();
            }
            if ((icon.Height > targetHeight) || (icon.Width > targetWidth))
            {
                using var bmp = icon.ToBitmap();
                return ResizeImage(bmp, targetWidth, targetHeight);
            }
            else
            {
                using var bmp = icon.ToBitmap();
                float scale = Math.Min((float)targetWidth / icon.Width, (float)targetHeight / icon.Height);
                return ScaleImage(bmp, (int)Math.Round(scale * icon.Width), (int)Math.Round(scale * icon.Height));
            }
        }

        private static float? _DpiScalingFactor;
        internal static float DpiScalingFactor
        {
            get
            {
                if (!_DpiScalingFactor.HasValue)
                {
                    using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
                    float dpiX = graphics.DpiX;
                    _DpiScalingFactor = dpiX / 96.0f;
                }
                return _DpiScalingFactor.Value;
            }
        }

        internal static void CenterControlInParent(Control control)
        {
            Control parent = control.Parent;
            control.Location = new Point(
                parent.Width / 2 - control.Width / 2,
                parent.Height / 2 - control.Height / 2);
        }

        internal static void FixupFormPosition(Form form)
        {
            Rectangle formVisibleArea = Rectangle.Intersect(SystemInformation.VirtualScreen, form.Bounds);
            if ((formVisibleArea.Width < 100) || (formVisibleArea.Height < 100))
                form.Location = Screen.PrimaryScreen.WorkingArea.Location;
        }

        internal static void Invoke(Control ctrl, MethodInvoker method)
        {
            if (ctrl.InvokeRequired)
                ctrl.BeginInvoke(method);
            else
                method.Invoke();
        }

        internal static DialogResult ShowMessageBox(string msg, string title, TaskDialogCommonButtons buttons, TaskDialogIcon icon, IWin32Window? parent = null)
        {
            Utils.SplitFirstLine(msg, out string firstLine, out string contentLines);

            var taskDialog = new TaskDialog();
            taskDialog.WindowTitle = title;
            taskDialog.MainInstruction = firstLine;
            taskDialog.CommonButtons = buttons;
            taskDialog.MainIcon = icon;
            taskDialog.Content = contentLines;
            if (parent is null)
                return (DialogResult)taskDialog.Show();
            else
                return (DialogResult)taskDialog.Show(parent);
        }

        internal static void SetDoubleBuffering(Control control, bool enable)
        {
            try
            {
                var prop = control.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                prop.SetValue(control, enable, null);
            }
            catch { }
        }
    }
}
