using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using pylorak.TinyWall.History;

namespace pylorak.TinyWall
{
    internal enum NotificationLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Toast notification facade. Provides the simple two-line "Notify"
    /// helper used everywhere in the app, plus the richer first-block
    /// toast (Phase 5) with Allow once / Allow always / Block always
    /// action buttons. Toast button clicks are dispatched via
    /// <see cref="ToastNotificationManagerCompat.OnActivated"/>.
    /// </summary>
    internal static class NotificationService
    {
        // Argument keys used in the toast launch string. Kept as
        // const so the producer (this class) and consumer
        // (HandleActivated) cannot drift.
        internal const string ArgKey_Action = "action";
        internal const string ArgKey_AppPath = "appPath";
        internal const string ArgKey_AppName = "appName";

        internal const string Action_OpenHistory = "openHistory";
        internal const string Action_AllowOnce = "allowOnce";
        internal const string Action_AllowAlways = "allowAlways";
        internal const string Action_BlockAlways = "blockAlways";

        private static Controller? _controller;
        private static bool _activatedHandlerWired;

        // Toast body clicks (vs button clicks) can fire from a background
        // thread — possibly even from a fresh process if Windows
        // COM-activated us. We can't safely create a Window from there.
        // Instead the activator pushes an "intent" onto this queue, and
        // App.OnPollTimer drains it on the UI thread once per tick.
        private static readonly ConcurrentQueue<ToastBodyClickIntent> _pendingBodyClicks = new();

        /// <summary>
        /// Wires up the toast button click dispatcher. Must be called
        /// once at app startup, after the Controller is constructed.
        /// </summary>
        internal static void RegisterFirstBlockHandler(Controller controller)
        {
            _controller = controller;
            if (_activatedHandlerWired) return;
            _activatedHandlerWired = true;

            try
            {
                ToastNotificationManagerCompat.OnActivated += OnToastActivated;
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        internal static void Notify(string message, NotificationLevel level = NotificationLevel.Info)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("PonyWall")
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
        /// Renders a first-block toast for the given app + flow. Three
        /// action buttons (Allow once / Allow always / Block always) are
        /// attached, and the toast body click navigates to History.
        /// </summary>
        internal static void ShowFirstBlockToast(FirstBlockToastInfo info)
        {
            try
            {
                string body = BuildBodyLine(info);

                var builder = new ToastContentBuilder()
                    .AddArgument(ArgKey_Action, Action_OpenHistory)
                    .AddArgument(ArgKey_AppPath, info.AppPath)
                    .AddArgument(ArgKey_AppName, info.AppName)
                    .AddText("Blocked: " + info.AppName)
                    .AddText(body)
                    .AddButton(new ToastButton()
                        .SetContent("Allow once")
                        .AddArgument(ArgKey_Action, Action_AllowOnce)
                        .AddArgument(ArgKey_AppPath, info.AppPath)
                        .AddArgument(ArgKey_AppName, info.AppName))
                    .AddButton(new ToastButton()
                        .SetContent("Allow always")
                        .AddArgument(ArgKey_Action, Action_AllowAlways)
                        .AddArgument(ArgKey_AppPath, info.AppPath)
                        .AddArgument(ArgKey_AppName, info.AppName))
                    .AddButton(new ToastButton()
                        .SetContent("Block always")
                        .AddArgument(ArgKey_Action, Action_BlockAlways)
                        .AddArgument(ArgKey_AppPath, info.AppPath)
                        .AddArgument(ArgKey_AppName, info.AppName));

                builder.Show();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private static string BuildBodyLine(FirstBlockToastInfo info)
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrEmpty(info.RemoteIp))
            {
                parts.Add(info.RemotePort > 0
                    ? $"{info.RemoteIp}:{info.RemotePort}"
                    : info.RemoteIp);
            }
            if (!string.IsNullOrEmpty(info.Protocol) && info.Protocol != "Any")
                parts.Add(info.Protocol);
            if (!string.IsNullOrEmpty(info.Direction) && info.Direction != "Any")
                parts.Add(info.Direction);

            string flow = parts.Count > 0 ? string.Join(" · ", parts) : "(no flow info)";
            return "Tried " + flow;
        }

        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
        {
            // The activator fires on a background thread. Marshal to the
            // UI thread before doing any controller work.
            try
            {
                var parsed = ToastArguments.Parse(args.Argument);
                string? action = parsed.Get(ArgKey_Action);
                string? appPath = parsed.Get(ArgKey_AppPath);
                string? appName = parsed.Get(ArgKey_AppName);

                if (string.IsNullOrEmpty(action))
                    return;

                Dispatcher.UIThread.Post(() => DispatchToastAction(action, appPath, appName));
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private static void DispatchToastAction(string action, string? appPath, string? appName)
        {
            try
            {
                switch (action)
                {
                    case Action_OpenHistory:
                        // Queue an intent for App.OnPollTimer to drain on
                        // the UI thread. Direct window creation here is
                        // unsafe — the activator can fire from a thread
                        // (or process) that has no UI loop yet.
                        _pendingBodyClicks.Enqueue(new ToastBodyClickIntent
                        {
                            AppPath = appPath,
                            AppName = appName,
                        });
                        return;

                    case Action_AllowOnce:
                    case Action_AllowAlways:
                    case Action_BlockAlways:
                        ApplyAppRule(action, appPath, appName);
                        return;
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                Notify("Toast action failed: " + ex.Message, NotificationLevel.Error);
            }
        }

        /// <summary>
        /// Drains any toast body-click intents accumulated since the
        /// previous call. The caller (App.OnPollTimer) is expected to
        /// open a HistoryWindow filtered to each one.
        /// </summary>
        internal static List<ToastBodyClickIntent> DrainBodyClickIntents()
        {
            var list = new List<ToastBodyClickIntent>();
            while (_pendingBodyClicks.TryDequeue(out var intent))
                list.Add(intent);
            return list;
        }

        private static void ApplyAppRule(string action, string? appPath, string? appName)
        {
            if (_controller == null || string.IsNullOrEmpty(appPath))
                return;

            // Allow once is a temporary rule (push via the dedicated
            // ADD_TEMPORARY_EXCEPTION channel). Allow/Block always are
            // permanent rules (PUT_SETTINGS round-trip).
            var subject = new ExecutableSubject(appPath);
            ExceptionPolicy policy = action switch
            {
                Action_AllowOnce => new TcpUdpPolicy(true),
                Action_AllowAlways => new UnrestrictedPolicy { LocalNetworkOnly = false },
                Action_BlockAlways => HardBlockPolicy.Instance,
                _ => throw new InvalidOperationException("unknown action: " + action),
            };
            var exception = new FirewallExceptionV3(subject, policy);

            if (action == Action_AllowOnce)
            {
                // No success notify: the user clicked an action button on a
                // toast. Popping another toast to confirm the first one worked
                // is redundant noise. Errors still notify below.
                _controller.AddTemporaryException(new[] { exception });
                return;
            }

            // Permanent rule path — fetch config, append, push back.
            Guid changeset = Guid.Empty;
            _controller.GetServerConfig(out var config, out _, ref changeset);
            if (config == null)
            {
                Notify("Could not contact firewall service.", NotificationLevel.Error);
                return;
            }

            var existing = ServiceGlobals.AppDatabase?.GetExceptionsForApp(subject, false, out _);
            var toAdd = (existing != null && existing.Count > 0)
                ? existing
                : new System.Collections.Generic.List<FirewallExceptionV3> { exception };

            // For block-always we override whatever the database says
            // about this app and force a hard-block.
            if (action == Action_BlockAlways)
                toAdd = new System.Collections.Generic.List<FirewallExceptionV3> { exception };

            // Drop any pre-existing exceptions for this subject so the
            // new rule replaces them. AddExceptions only merges policies
            // it knows how to merge — a HardBlock landing on top of an
            // existing Unrestricted rule would otherwise leave both
            // present and produce confusing behavior. The user clicked a
            // toast button, so their intent for this subject is explicit.
            config.ActiveProfile.AppExceptions.RemoveAll(ex => ex.Subject.Equals(subject));

            config.ActiveProfile.AddExceptions(toAdd);
            var resp = _controller.SetServerConfig(config, changeset);
            if (resp.Type != MessageType.PUT_SETTINGS)
            {
                // Error still surfaces — silent failure would leave the user
                // with no idea the toast button didn't take effect. Success
                // is intentionally silent (see AllowOnce comment above).
                Notify("Firewall rule update failed.", NotificationLevel.Error);
            }
        }

        /// <summary>
        /// One pending toast body-click. Created by the activator,
        /// drained by the UI poll timer.
        /// </summary>
        internal sealed class ToastBodyClickIntent
        {
            public string? AppPath { get; init; }
            public string? AppName { get; init; }
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
