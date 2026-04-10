using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace pylorak.TinyWall
{
    internal enum NotificationLevel
    {
        Info,
        Warning,
        Error
    }

    internal static class NotificationService
    {
        internal static void Notify(string message, NotificationLevel level = NotificationLevel.Info)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("TinyWall")
                    .AddText(message)
                    .Show();
            }
            catch (Exception ex)
            {
                // Toast infrastructure may not be available — log but don't re-notify
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        /// <summary>
        /// Call on app shutdown to clean up the toast notification system.
        /// </summary>
        internal static void Cleanup()
        {
            try
            {
                ToastNotificationManagerCompat.Uninstall();
            }
            catch { }
        }
    }
}
