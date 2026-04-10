using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace pylorak.TinyWall.Views
{
    /// <summary>
    /// Shared helper that reads/writes Avalonia window state (location, size,
    /// maximized/normal, data-grid column widths) to the persisted
    /// <see cref="ControllerSettings"/> record.
    /// </summary>
    internal static class WindowStatePersistence
    {
        /// <summary>
        /// Returns the current <see cref="ActiveConfig.Controller"/>, lazily
        /// loading it from disk on first access so individual windows do not
        /// need to worry about ordering vs App initialization.
        /// </summary>
        internal static ControllerSettings GetOrLoadController()
        {
            var ctrl = ActiveConfig.Controller;
            if (ctrl == null)
            {
                ctrl = ControllerSettings.Load();
                ActiveConfig.Controller = ctrl;
            }
            return ctrl;
        }

        /// <summary>
        /// Applies a previously-saved location/size/state to the given window.
        /// Zero-valued sizes are treated as "no saved value" and left at the
        /// XAML defaults. Minimized is restored as Normal.
        /// </summary>
        internal static void Restore(Window window, int locX, int locY, int width, int height, WindowStateValue state)
        {
            // Size
            if (width > 0 && height > 0)
            {
                window.Width = width;
                window.Height = height;
            }

            // Position — only apply if something was stored (both zero means "not set")
            if (locX != 0 || locY != 0)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(locX, locY);
            }

            // Maximized wins over size; Minimized is not useful on re-open.
            window.WindowState = state == WindowStateValue.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        /// <summary>
        /// Captures the current window location/size/state. For Maximized
        /// windows the width/height are left untouched (so the pre-maximize
        /// size is preserved across restarts), but the position is always
        /// written back.
        /// </summary>
        internal static void Capture(
            Window window,
            ref int locX,
            ref int locY,
            ref int width,
            ref int height,
            ref WindowStateValue state)
        {
            var pos = window.Position;
            locX = pos.X;
            locY = pos.Y;

            state = window.WindowState switch
            {
                WindowState.Maximized => WindowStateValue.Maximized,
                WindowState.Minimized => WindowStateValue.Minimized,
                _ => WindowStateValue.Normal,
            };

            if (window.WindowState != WindowState.Maximized)
            {
                // Width/Height are doubles in Avalonia; round for storage.
                if (!double.IsNaN(window.Width) && window.Width > 0)
                    width = (int)Math.Round(window.Width);
                if (!double.IsNaN(window.Height) && window.Height > 0)
                    height = (int)Math.Round(window.Height);
            }
        }

        /// <summary>
        /// Copies the current DataGrid column widths (keyed by header text) into
        /// the given dictionary. Columns whose header is not a string are skipped.
        /// </summary>
        internal static void CaptureColumnWidths(DataGrid? grid, Dictionary<string, int> target)
        {
            if (grid == null) return;

            foreach (var col in grid.Columns)
            {
                if (col.Header is not string key || string.IsNullOrEmpty(key))
                    continue;

                var w = col.ActualWidth;
                if (double.IsNaN(w) || w <= 0)
                    continue;

                target[key] = (int)Math.Round(w);
            }
        }

        /// <summary>
        /// Applies persisted column widths to a DataGrid. Columns whose header
        /// has no saved entry are left at their XAML default.
        /// </summary>
        internal static void RestoreColumnWidths(DataGrid? grid, Dictionary<string, int>? source)
        {
            if (grid == null || source == null || source.Count == 0) return;

            foreach (var col in grid.Columns)
            {
                if (col.Header is not string key || string.IsNullOrEmpty(key))
                    continue;

                if (source.TryGetValue(key, out int w) && w > 0)
                    col.Width = new DataGridLength(w);
            }
        }
    }
}
