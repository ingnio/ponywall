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
        // Flow tuple carried on the Allow/Block buttons so the activator
        // can reconstruct a scoped RuleDef at click time (see the Scope_
        // handling in DispatchToastAction). These are read verbatim from
        // FirstBlockToastInfo in ShowFirstBlockToast.
        internal const string ArgKey_RemoteIp = "remoteIp";
        internal const string ArgKey_RemotePort = "remotePort";
        internal const string ArgKey_Protocol = "protocol";
        internal const string ArgKey_Direction = "direction";

        internal const string Action_OpenHistory = "openHistory";
        internal const string Action_Allow = "allow";
        internal const string Action_Block = "block";

        // Combo box IDs. Two separate boxes so the activator can read the
        // scope for the clicked action without worrying about the other
        // box's current value leaking into the decision.
        internal const string ComboId_AllowScope = "allowScope";
        internal const string ComboId_BlockScope = "blockScope";

        // Scope item IDs. Machine identifiers — NOT localized. Display
        // strings come from Messages.resx via FirstBlockNotif_*.
        internal const string Scope_Once = "once";
        internal const string Scope_ThisDest = "thisDest";
        internal const string Scope_Anywhere = "anywhere";

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
        /// Renders a first-block toast for the given app + flow. Two
        /// action buttons (Allow / Block) are attached, each paired with
        /// a scope combo box so the user can pick how broad the rule
        /// should be. The toast body click still navigates to History.
        /// Selection-box values come back to the activator via
        /// <see cref="ToastNotificationActivatedEventArgsCompat.UserInput"/>.
        /// </summary>
        internal static void ShowFirstBlockToast(FirstBlockToastInfo info)
        {
            try
            {
                string body = BuildBodyLine(info);
                // Show the full path in smaller text below the flow line
                // so the user can distinguish e.g. two different tailscale
                // binaries or a legit vs. suspicious exe with the same name.
                string pathLine = string.IsNullOrEmpty(info.AppPath) ? string.Empty : info.AppPath;
                string remotePortStr = info.RemotePort.ToString(CultureInfo.InvariantCulture);

                var allowOnce = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_JustThisTime ?? "Just this time";
                var scopeThisDest = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_ThisDestinationOnly ?? "This destination only";
                var scopeAnywhere = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_Anywhere ?? "Anywhere";
                var allowScopeLabel = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_AllowScope ?? "Allow scope";
                var blockScopeLabel = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_BlockScope ?? "Block scope";
                var allowBtnLabel = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_Allow ?? "Allow";
                var blockBtnLabel = pylorak.TinyWall.Resources.Messages.FirstBlockNotif_Block ?? "Block";

                var builder = new ToastContentBuilder()
                    // Body-click intent: opens HistoryWindow filtered to
                    // this app. Preserved from the old three-button layout.
                    .AddArgument(ArgKey_Action, Action_OpenHistory)
                    .AddArgument(ArgKey_AppPath, info.AppPath)
                    .AddArgument(ArgKey_AppName, info.AppName)
                    .AddText("Blocked: " + info.AppName)
                    .AddText(body)
                    .AddAttributionText(pathLine)
                    // Allow scope combo — three items. Default is the user's
                    // last-used selection (persisted in ControllerSettings),
                    // falling back to "Just this time" if no prior choice.
                    .AddComboBox(
                        ComboId_AllowScope,
                        allowScopeLabel,
                        GetLastAllowScope(),
                        (Scope_Once, allowOnce),
                        (Scope_ThisDest, scopeThisDest),
                        (Scope_Anywhere, scopeAnywhere))
                    // Block scope combo — two items. Default is the user's
                    // last-used selection, falling back to "This destination
                    // only" (least-collateral option).
                    .AddComboBox(
                        ComboId_BlockScope,
                        blockScopeLabel,
                        GetLastBlockScope(),
                        (Scope_ThisDest, scopeThisDest),
                        (Scope_Anywhere, scopeAnywhere))
                    // Allow/Block buttons — carry the action + flow tuple.
                    // The scope is read from UserInput at activation time.
                    .AddButton(new ToastButton()
                        .SetContent(allowBtnLabel)
                        .AddArgument(ArgKey_Action, Action_Allow)
                        .AddArgument(ArgKey_AppPath, info.AppPath)
                        .AddArgument(ArgKey_AppName, info.AppName)
                        .AddArgument(ArgKey_RemoteIp, info.RemoteIp ?? string.Empty)
                        .AddArgument(ArgKey_RemotePort, remotePortStr)
                        .AddArgument(ArgKey_Protocol, info.Protocol ?? string.Empty)
                        .AddArgument(ArgKey_Direction, info.Direction ?? string.Empty))
                    .AddButton(new ToastButton()
                        .SetContent(blockBtnLabel)
                        .AddArgument(ArgKey_Action, Action_Block)
                        .AddArgument(ArgKey_AppPath, info.AppPath)
                        .AddArgument(ArgKey_AppName, info.AppName)
                        .AddArgument(ArgKey_RemoteIp, info.RemoteIp ?? string.Empty)
                        .AddArgument(ArgKey_RemotePort, remotePortStr)
                        .AddArgument(ArgKey_Protocol, info.Protocol ?? string.Empty)
                        .AddArgument(ArgKey_Direction, info.Direction ?? string.Empty));

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

                // Parse the flow tuple off the button (empty strings for
                // body-click activations, which don't carry these args).
                string? remoteIp = parsed.Get(ArgKey_RemoteIp);
                string remotePortStr = parsed.Get(ArgKey_RemotePort) ?? "0";
                int remotePort = int.TryParse(remotePortStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;
                string? protocol = parsed.Get(ArgKey_Protocol);
                string? direction = parsed.Get(ArgKey_Direction);

                // Pick the scope combo box that matches the clicked action.
                // Reading only the relevant one avoids a stale value from
                // the other combo leaking into the decision.
                string? scope = null;
                if (action == Action_Allow)
                    scope = args.UserInput.TryGetValue(ComboId_AllowScope, out var av) ? av?.ToString() : null;
                else if (action == Action_Block)
                    scope = args.UserInput.TryGetValue(ComboId_BlockScope, out var bv) ? bv?.ToString() : null;

                var ctx = new ToastClickContext
                {
                    Action = action,
                    Scope = scope,
                    AppPath = appPath,
                    AppName = appName,
                    RemoteIp = remoteIp,
                    RemotePort = remotePort,
                    Protocol = protocol,
                    Direction = direction,
                };

                Dispatcher.UIThread.Post(() => DispatchToastAction(ctx));
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        /// <summary>
        /// Parsed toast activation context. Built on the background
        /// activator thread, dispatched to the UI thread for the actual
        /// firewall work.
        /// </summary>
        private sealed class ToastClickContext
        {
            public string Action { get; init; } = string.Empty;
            public string? Scope { get; init; }
            public string? AppPath { get; init; }
            public string? AppName { get; init; }
            public string? RemoteIp { get; init; }
            public int RemotePort { get; init; }
            public string? Protocol { get; init; }
            public string? Direction { get; init; }
        }

        private static void DispatchToastAction(ToastClickContext ctx)
        {
            try
            {
                switch (ctx.Action)
                {
                    case Action_OpenHistory:
                        // Queue an intent for App.OnPollTimer to drain on
                        // the UI thread. Direct window creation here is
                        // unsafe — the activator can fire from a thread
                        // (or process) that has no UI loop yet.
                        _pendingBodyClicks.Enqueue(new ToastBodyClickIntent
                        {
                            AppPath = ctx.AppPath,
                            AppName = ctx.AppName,
                        });
                        return;

                    case Action_Allow:
                    case Action_Block:
                        ApplyAppRule(ctx);
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

        private static void ApplyAppRule(ToastClickContext ctx)
        {
            if (_controller == null || string.IsNullOrEmpty(ctx.AppPath))
                return;

            // Persist the scope the user chose so the next toast defaults
            // to the same selection — saves repetitive dropdown changes
            // for users who always pick the same scope.
            PersistScopeChoice(ctx.Action, ctx.Scope);

            var subject = new ExecutableSubject(ctx.AppPath);

            // Allow + "Just this time" is the only session-scoped path;
            // everything else is a permanent rule pushed via PUT_SETTINGS.
            if (ctx.Action == Action_Allow && ctx.Scope == Scope_Once)
            {
                // Temporary exception — narrow by protocol/port isn't
                // expressed here. Matches the previous "Allow once"
                // behavior (TcpUdpPolicy unrestricted, session-only).
                var tempException = new FirewallExceptionV3(subject, new TcpUdpPolicy(unrestricted: true));
                _controller.AddTemporaryException(new[] { tempException });
                return;
            }

            // Build the policy for the permanent path. The scoped branch
            // builds a RuleListPolicy with one RuleDef narrowed by the
            // flow tuple carried on the button. Anywhere branches reuse
            // the old unconditional policies.
            ExceptionPolicy policy;
            bool replaceExisting;
            switch ((ctx.Action, ctx.Scope))
            {
                case (Action_Allow, Scope_ThisDest):
                    policy = BuildScopedRuleListPolicy(RuleAction.Allow, ctx);
                    replaceExisting = false; // accumulate scoped rules
                    break;
                case (Action_Allow, Scope_Anywhere):
                    policy = new UnrestrictedPolicy { LocalNetworkOnly = false };
                    replaceExisting = true;  // broad rule supersedes everything
                    break;
                case (Action_Block, Scope_ThisDest):
                    policy = BuildScopedRuleListPolicy(RuleAction.Block, ctx);
                    replaceExisting = false;
                    break;
                case (Action_Block, Scope_Anywhere):
                    policy = HardBlockPolicy.Instance;
                    replaceExisting = true;
                    break;
                default:
                    // Unknown (action, scope) combination — either an
                    // in-flight toast from an older build or a UserInput
                    // lookup that returned null. Bail with an error toast
                    // so the user can retry on the fresh toast.
                    Notify($"Unknown toast action/scope: {ctx.Action}/{ctx.Scope}", NotificationLevel.Error);
                    return;
            }

            var exception = new FirewallExceptionV3(subject, policy);

            Guid changeset = Guid.Empty;
            _controller.GetServerConfig(out var config, out _, ref changeset);
            if (config == null)
            {
                Notify("Could not contact firewall service.", NotificationLevel.Error);
                return;
            }

            if (replaceExisting)
            {
                // Drop any pre-existing exceptions for this subject so the
                // new broad rule replaces them. Prevents a HardBlock from
                // coexisting with an older Unrestricted rule for the same
                // app and producing confusing behavior.
                config.ActiveProfile.AppExceptions.RemoveAll(ex => ex.Subject.Equals(subject));
            }

            config.ActiveProfile.AddExceptions(new System.Collections.Generic.List<FirewallExceptionV3> { exception });
            var resp = _controller.SetServerConfig(config, changeset);
            if (resp.Type != MessageType.PUT_SETTINGS)
            {
                // Error still surfaces — silent failure would leave the user
                // with no idea the toast button didn't take effect. Success
                // is intentionally silent (matches the Settings OK and
                // HistoryWindow create-exception conventions).
                Notify("Firewall rule update failed.", NotificationLevel.Error);
            }
        }

        /// <summary>
        /// Builds a RuleListPolicy with a single RuleDef narrowed by the
        /// flow tuple carried on a toast click (remote IP + port +
        /// protocol + direction). Used for both Allow→"This destination
        /// only" and Block→"This destination only". Local port is
        /// intentionally NOT included in the narrowing — outbound TCP
        /// flows pick a new ephemeral local port every connection, so
        /// pinning on LocalPort would make the rule match at most one
        /// socket lifetime.
        /// </summary>
        private static RuleListPolicy BuildScopedRuleListPolicy(RuleAction action, ToastClickContext ctx)
        {
            Protocol protocol = ParseProtocol(ctx.Protocol);
            RuleDirection direction = ParseDirection(ctx.Direction);

            var rule = new RuleDef
            {
                Name = (action == RuleAction.Allow ? "Allow " : "Block ")
                       + (ctx.AppName ?? "app") + " to "
                       + (string.IsNullOrEmpty(ctx.RemoteIp) ? "unknown" : ctx.RemoteIp),
                Action = action,
                Application = ctx.AppPath,
                // Null = any when not specified; empty flow fields from
                // the toast (e.g. no remote IP extracted from a raw WFP
                // event) fall through to the per-field null treatment.
                RemoteAddresses = string.IsNullOrEmpty(ctx.RemoteIp) ? null : ctx.RemoteIp,
                // Per the plan: RemotePort==0 is non-TCP/UDP (ICMP etc.)
                // and we fall back to any-port narrowed by Protocol alone.
                RemotePorts = ctx.RemotePort > 0
                    ? ctx.RemotePort.ToString(CultureInfo.InvariantCulture)
                    : null,
                Protocol = protocol,
                Direction = direction,
            };

            return new RuleListPolicy { Rules = new System.Collections.Generic.List<RuleDef> { rule } };
        }

        private static Protocol ParseProtocol(string? s)
        {
            if (string.IsNullOrEmpty(s)) return Protocol.Any;
            // Try enum-by-name first (matches the strings the service
            // puts into FirstBlockToastInfo.Protocol — see
            // FirewallLogEntry.Protocol.ToString()).
            if (Enum.TryParse<Protocol>(s, ignoreCase: true, out var parsed))
                return parsed;
            return Protocol.Any;
        }

        private static RuleDirection ParseDirection(string? s)
        {
            if (string.IsNullOrEmpty(s)) return RuleDirection.InOut;
            if (Enum.TryParse<RuleDirection>(s, ignoreCase: true, out var parsed))
                return parsed;
            return RuleDirection.InOut;
        }

        // =================================================================
        // Persisted scope defaults — read/write via ControllerSettings
        // =================================================================

        private static string GetLastAllowScope()
        {
            var s = ActiveConfig.Controller?.LastAllowScope;
            // Validate — only return a known value, otherwise fall back.
            return s == Scope_Once || s == Scope_ThisDest || s == Scope_Anywhere
                ? s : Scope_Once;
        }

        private static string GetLastBlockScope()
        {
            var s = ActiveConfig.Controller?.LastBlockScope;
            return s == Scope_ThisDest || s == Scope_Anywhere
                ? s : Scope_ThisDest;
        }

        private static void PersistScopeChoice(string action, string? scope)
        {
            var ctrl = ActiveConfig.Controller;
            if (ctrl == null) return;

            if (action == Action_Allow && scope != null)
                ctrl.LastAllowScope = scope;
            else if (action == Action_Block && scope != null)
                ctrl.LastBlockScope = scope;

            try { ctrl.Save(); } catch { }
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
