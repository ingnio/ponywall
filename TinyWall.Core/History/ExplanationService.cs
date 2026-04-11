using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Post-hoc firewall decision explainer. Given a captured event and
    /// the ruleset that was active at the time, this walks the ruleset in
    /// the same order the kernel would have evaluated it and reconstructs
    /// the most likely reason. See Docs/EXPLAINABILITY.md section 6.
    ///
    /// Because WFP does not expose its evaluation trace (see section 2),
    /// everything here is a best-effort replay. The result carries a
    /// <see cref="Confidence"/> flag so the UI can de-emphasize guesses.
    /// </summary>
    public sealed class ExplanationService : IExplanationService
    {
        private const int BackfillBatchSize = 100;
        private const int BackfillMaxPerPass = 1000;

        private readonly IFirewallEventStore _store;

        // Cache of (rulesetId -> decoded ServerConfiguration). The backfill
        // job typically hits the same ruleset for thousands of rows in a
        // row, so avoid re-deserializing the JSON per event.
        private readonly Dictionary<long, ServerConfiguration?> _snapshotCache = new();
        private readonly object _cacheLock = new();

        public ExplanationService(IFirewallEventStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public Explanation Explain(long decisionId)
        {
            var record = _store.GetEventById(decisionId);
            if (record is null)
                return Explanation.Empty(ReasonId.Unknown, Confidence.Low);

            var config = LoadSnapshotConfig(record.RulesetId);
            if (config is null)
                return Explanation.Empty(ReasonId.Unknown, Confidence.Low);

            return ExplainAgainst(record, config, record.ModeAtEvent);
        }

        public Explanation ExplainFlow(FlowFingerprint flow)
        {
            // Simulate an event from the fingerprint using a fake record
            // with "now" as the timestamp and the most recent ruleset.
            var fakeRecord = new FirewallEventRecord
            {
                Direction = flow.Direction,
                Protocol = flow.Protocol,
                LocalIp = flow.LocalIp,
                LocalPort = flow.LocalPort,
                RemoteIp = flow.RemoteIp,
                RemotePort = flow.RemotePort,
                AppPath = flow.AppPath,
                AppName = flow.AppPath is null ? null : Path.GetFileName(flow.AppPath),
                TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Action = EventAction.Block, // by default; fast paths may override
                ModeAtEvent = FirewallMode.Normal,
                RulesetId = 0,
            };
            // ExplainFlow is a simulation: we don't have an event row, so
            // callers who want a historical ruleset should instead call
            // Explain(decisionId). Return Unknown with Low confidence
            // when we have no ruleset to replay against.
            return Explanation.Empty(ReasonId.Unknown, Confidence.Low);
        }

        public void Backfill(CancellationToken cancel)
        {
            int totalDone = 0;
            while (totalDone < BackfillMaxPerPass && !cancel.IsCancellationRequested)
            {
                IReadOnlyList<FirewallEventRecord> batch;
                try
                {
                    batch = _store.GetUnexplainedBatch(BackfillBatchSize);
                }
                catch (Exception ex)
                {
                    Utils.LogException(ex, Utils.LOG_ID_SERVICE);
                    return;
                }

                if (batch.Count == 0)
                    return;

                var updates = new List<ReasonUpdate>(batch.Count);
                foreach (var record in batch)
                {
                    if (cancel.IsCancellationRequested)
                        break;

                    Explanation exp;
                    try
                    {
                        var config = LoadSnapshotConfig(record.RulesetId);
                        exp = config is null
                            ? Explanation.Empty(ReasonId.Unknown, Confidence.Low)
                            : ExplainAgainst(record, config, record.ModeAtEvent);
                    }
                    catch (Exception ex)
                    {
                        Utils.LogException(ex, Utils.LOG_ID_SERVICE);
                        exp = Explanation.Empty(ReasonId.Unknown, Confidence.Low);
                    }

                    // Never leave a row stuck at Unknown forever — force
                    // an Unknown marker with low confidence so the
                    // backfill job won't reconsider it next pass.
                    var writeReason = exp.PrimaryReason == ReasonId.Unknown
                        ? ReasonId.BlockedSubjectNotResolvable
                        : exp.PrimaryReason;
                    var writeConf = exp.PrimaryReason == ReasonId.Unknown
                        ? Confidence.Low
                        : exp.Confidence;

                    string? nearMissIds = exp.NearMisses.Count == 0
                        ? null
                        : string.Join(",", exp.NearMisses.Take(3).Select(n => n.RuleId));

                    updates.Add(new ReasonUpdate(
                        record.Id,
                        writeReason,
                        writeConf,
                        exp.MatchedRuleId,
                        nearMissIds));
                }

                if (updates.Count > 0)
                {
                    try
                    {
                        _store.UpdateReasons(updates);
                    }
                    catch (Exception ex)
                    {
                        Utils.LogException(ex, Utils.LOG_ID_SERVICE);
                        return;
                    }
                }

                totalDone += batch.Count;

                if (batch.Count < BackfillBatchSize)
                    return;
            }
        }

        // ----- Pure, testable core ---------------------------------------

        /// <summary>
        /// Pure explanation against a supplied config + mode. Takes no DB
        /// dependency, does no network I/O, and does not mutate anything.
        /// This is what the tests exercise.
        /// </summary>
        public static Explanation ExplainAgainst(FirewallEventRecord record, ServerConfiguration config, FirewallMode modeAtEvent)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            if (config is null) throw new ArgumentNullException(nameof(config));

            // Medium confidence when we have a resolved ruleset. The UI
            // can downgrade to Low if it can't resolve the subject file.
            var confidence = Confidence.Medium;
            var evidence = BuildEvidenceChips(record, modeAtEvent);

            // ---- Mode-level fast paths ---------------------------------
            if (record.Action == EventAction.Block)
            {
                switch (modeAtEvent)
                {
                    case FirewallMode.BlockAll:
                        return BuildExplanation(
                            ReasonId.BlockedByModeBlockAll,
                            confidence,
                            null, null,
                            Array.Empty<NearMiss>(),
                            evidence,
                            RemediationsForReason(ReasonId.BlockedByModeBlockAll, record));

                    case FirewallMode.Disabled:
                        return BuildExplanation(
                            ReasonId.BlockedByModeDisabled,
                            confidence,
                            null, null,
                            Array.Empty<NearMiss>(),
                            evidence,
                            RemediationsForReason(ReasonId.BlockedByModeDisabled, record));
                }
            }

            // Active profile may not exist if the snapshot predates the
            // named profile — degrade gracefully.
            ServerProfileConfiguration? profile;
            try
            {
                profile = string.IsNullOrEmpty(config.ActiveProfileName) ? null : config.ActiveProfile;
            }
            catch
            {
                profile = null;
            }

            IReadOnlyList<FirewallExceptionV3> exceptions = profile?.AppExceptions
                ?? (IReadOnlyList<FirewallExceptionV3>)Array.Empty<FirewallExceptionV3>();

            // ---- Find the rule, if any, that would have matched -------

            var subjectMatches = new List<FirewallExceptionV3>();
            foreach (var ex in exceptions)
            {
                if (SubjectMatches(ex.Subject, record))
                    subjectMatches.Add(ex);
            }

            // 1) Subject matched AND policy says Block -> BlockedByMatchedBlockRule
            // 2) Subject matched AND policy says Allow with right proto/port/dir -> Allow
            // 3) Subject matched AND policy Allow but wrong port/proto/dir -> BlockedRestrictedPorts
            // 4) No subject match, mode=Normal, event=Block -> BlockedNoMatchInNormal

            FirewallExceptionV3? chosen = null;
            ReasonId? chosenReason = null;
            bool sawHardBlock = false;
            bool sawPortMismatch = false;
            bool sawDirectionMismatch = false;
            bool sawLocalNetworkOnly = false;
            FirewallExceptionV3? hardBlockMatch = null;

            foreach (var ex in subjectMatches)
            {
                switch (ex.Policy.PolicyType)
                {
                    case PolicyType.HardBlock:
                        sawHardBlock = true;
                        hardBlockMatch ??= ex;
                        break;

                    case PolicyType.Unrestricted:
                        var upol = (UnrestrictedPolicy)ex.Policy;
                        if (upol.LocalNetworkOnly && !IsLocalNetworkRemote(record.RemoteIp))
                        {
                            sawLocalNetworkOnly = true;
                            continue;
                        }
                        // Any protocol, any port, any direction -> allow.
                        chosen ??= ex;
                        chosenReason = ReasonId.AllowedByMatchedRule;
                        break;

                    case PolicyType.TcpUdpOnly:
                        var tpol = (TcpUdpPolicy)ex.Policy;
                        var tmr = MatchTcpUdpPolicy(tpol, record);
                        if (tmr == TcpUdpMatchResult.Allow)
                        {
                            chosen ??= ex;
                            chosenReason = ReasonId.AllowedByMatchedRule;
                        }
                        else
                        {
                            if (tmr == TcpUdpMatchResult.WrongPort) sawPortMismatch = true;
                            if (tmr == TcpUdpMatchResult.WrongDirection) sawDirectionMismatch = true;
                            if (tmr == TcpUdpMatchResult.WrongLocalNetwork) sawLocalNetworkOnly = true;
                        }
                        break;

                    case PolicyType.RuleList:
                        var rpol = (RuleListPolicy)ex.Policy;
                        var rres = MatchRuleList(rpol, record);
                        if (rres == RuleListMatchResult.Allow)
                        {
                            chosen ??= ex;
                            chosenReason = ReasonId.AllowedByMatchedRule;
                        }
                        else if (rres == RuleListMatchResult.Block)
                        {
                            if (chosen is null)
                            {
                                chosen = ex;
                                chosenReason = ReasonId.BlockedByMatchedBlockRule;
                            }
                        }
                        else
                        {
                            if (rres == RuleListMatchResult.WrongPort) sawPortMismatch = true;
                            if (rres == RuleListMatchResult.WrongDirection) sawDirectionMismatch = true;
                        }
                        break;
                }

                // Allow beats Block if both were seen — honor the first
                // Allow we hit and stop scanning.
                if (chosenReason == ReasonId.AllowedByMatchedRule)
                    break;
            }

            // Allow rule prevailed
            if (chosen is not null && chosenReason == ReasonId.AllowedByMatchedRule)
            {
                var matchedEvidence = evidence.ToList();
                matchedEvidence.Add(new EvidenceChip(EvidenceKind.Rule,
                    $"matched: {chosen.Subject}",
                    chosen.Id.ToString(),
                    ChipSeverity.Good,
                    FilterKey: "matched_rule_id"));
                return BuildExplanation(
                    ReasonId.AllowedByMatchedRule,
                    confidence,
                    chosen.Id.ToString(),
                    (chosen.Subject.ToString() ?? "(unknown)"),
                    Array.Empty<NearMiss>(),
                    matchedEvidence,
                    RemediationsForReason(ReasonId.AllowedByMatchedRule, record));
            }

            // Explicit block rule matched
            if (sawHardBlock && hardBlockMatch is not null)
            {
                return BuildExplanation(
                    ReasonId.BlockedByMatchedBlockRule,
                    confidence,
                    hardBlockMatch.Id.ToString(),
                    (hardBlockMatch.Subject.ToString() ?? "(unknown)"),
                    // The matched block rule is itself a "near-allow" —
                    // show the user what they'd have to change.
                    new[] { new NearMiss(
                        hardBlockMatch.Id.ToString(),
                        (hardBlockMatch.Subject.ToString() ?? "(unknown)"),
                        "This rule explicitly blocks this app.") },
                    evidence,
                    RemediationsForReason(ReasonId.BlockedByMatchedBlockRule, record));
            }

            // RuleList matched a Block entry
            if (chosen is not null && chosenReason == ReasonId.BlockedByMatchedBlockRule)
            {
                return BuildExplanation(
                    ReasonId.BlockedByMatchedBlockRule,
                    confidence,
                    chosen.Id.ToString(),
                    (chosen.Subject.ToString() ?? "(unknown)"),
                    Array.Empty<NearMiss>(),
                    evidence,
                    RemediationsForReason(ReasonId.BlockedByMatchedBlockRule, record));
            }

            // Subject matched but policy rejected this flow
            if (subjectMatches.Count > 0 && record.Action == EventAction.Block)
            {
                var reason =
                    sawPortMismatch ? ReasonId.BlockedRestrictedPorts :
                    sawDirectionMismatch ? ReasonId.BlockedWrongDirection :
                    sawLocalNetworkOnly ? ReasonId.BlockedRestrictedLocalNetwork :
                    ReasonId.BlockedRestrictedPorts;

                var nearMisses = BuildNearMisses(subjectMatches, record);
                return BuildExplanation(
                    reason,
                    confidence,
                    null, null,
                    nearMisses,
                    evidence,
                    RemediationsForReason(reason, record));
            }

            // Nothing matched subject
            if (record.Action == EventAction.Block && modeAtEvent == FirewallMode.Normal)
            {
                var nearMisses = BuildNearMissesByApp(exceptions, record);
                return BuildExplanation(
                    ReasonId.BlockedNoMatchInNormal,
                    confidence,
                    null, null,
                    nearMisses,
                    evidence,
                    RemediationsForReason(ReasonId.BlockedNoMatchInNormal, record));
            }

            // Allowed event with no explicit matching rule: most likely the
            // default-allow modes (AllowOutgoing / Learning).
            if (record.Action == EventAction.Allow)
            {
                var reason = modeAtEvent switch
                {
                    FirewallMode.AllowOutgoing => ReasonId.AllowedByOutgoingMode,
                    FirewallMode.Learning => ReasonId.AllowedByLearningMode,
                    _ => ReasonId.AllowedByMatchedRule,
                };
                return BuildExplanation(
                    reason,
                    confidence,
                    null, null,
                    Array.Empty<NearMiss>(),
                    evidence,
                    RemediationsForReason(reason, record));
            }

            return Explanation.Empty(ReasonId.Unknown, Confidence.Low);
        }

        // ----- Helpers: rule matching -----------------------------------

        private static bool SubjectMatches(ExceptionSubject subject, FirewallEventRecord record)
        {
            if (subject is GlobalSubject) return true;

            if (subject is ServiceSubject service)
            {
                // Service match is path + named service. We only have the
                // service name on the event if the log parser captured it.
                if (!PathsEqual(service.ExecutablePath, record.AppPath))
                    return false;
                if (record.ServiceName is null)
                    return false;
                return string.Equals(service.ServiceName, record.ServiceName, StringComparison.OrdinalIgnoreCase);
            }

            if (subject is ExecutableSubject exe)
                return PathsEqual(exe.ExecutablePath, record.AppPath);

            if (subject is AppContainerSubject uwp)
                return record.PackageSid is not null
                    && string.Equals(uwp.Sid, record.PackageSid, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static bool PathsEqual(string? a, string? b)
        {
            if (a is null || b is null) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private enum TcpUdpMatchResult
        {
            Allow,
            WrongPort,
            WrongDirection,
            WrongLocalNetwork,
            WrongProtocol,
        }

        private static TcpUdpMatchResult MatchTcpUdpPolicy(TcpUdpPolicy pol, FirewallEventRecord r)
        {
            if (r.Protocol != Protocol.TCP && r.Protocol != Protocol.UDP && r.Protocol != Protocol.TcpUdp)
                return TcpUdpMatchResult.WrongProtocol;

            if (pol.LocalNetworkOnly && !IsLocalNetworkRemote(r.RemoteIp))
                return TcpUdpMatchResult.WrongLocalNetwork;

            bool inbound = (r.Direction & RuleDirection.In) != 0;
            bool outbound = (r.Direction & RuleDirection.Out) != 0;

            // Outbound connect — match against AllowedRemote*ConnectPorts
            if (outbound)
            {
                string? ports = r.Protocol == Protocol.TCP
                    ? pol.AllowedRemoteTcpConnectPorts
                    : pol.AllowedRemoteUdpConnectPorts;
                if (string.IsNullOrEmpty(ports)) return TcpUdpMatchResult.WrongDirection;
                return PortMatches(ports, r.RemotePort)
                    ? TcpUdpMatchResult.Allow
                    : TcpUdpMatchResult.WrongPort;
            }

            if (inbound)
            {
                string? ports = r.Protocol == Protocol.TCP
                    ? pol.AllowedLocalTcpListenerPorts
                    : pol.AllowedLocalUdpListenerPorts;
                if (string.IsNullOrEmpty(ports)) return TcpUdpMatchResult.WrongDirection;
                return PortMatches(ports, r.LocalPort)
                    ? TcpUdpMatchResult.Allow
                    : TcpUdpMatchResult.WrongPort;
            }

            return TcpUdpMatchResult.WrongDirection;
        }

        private enum RuleListMatchResult
        {
            Allow,
            Block,
            WrongPort,
            WrongDirection,
            WrongProtocol,
            NoMatch,
        }

        private static RuleListMatchResult MatchRuleList(RuleListPolicy pol, FirewallEventRecord r)
        {
            var result = RuleListMatchResult.NoMatch;
            foreach (var rule in pol.Rules)
            {
                bool protoOk = rule.Protocol == Protocol.Any
                    || rule.Protocol == r.Protocol
                    || (rule.Protocol == Protocol.TcpUdp && (r.Protocol == Protocol.TCP || r.Protocol == Protocol.UDP));
                bool dirOk = (rule.Direction & r.Direction) != 0
                    || rule.Direction == RuleDirection.InOut;
                bool localPortOk = string.IsNullOrEmpty(rule.LocalPorts) || PortMatches(rule.LocalPorts!, r.LocalPort);
                bool remotePortOk = string.IsNullOrEmpty(rule.RemotePorts) || PortMatches(rule.RemotePorts!, r.RemotePort);

                if (!protoOk) { if (result == RuleListMatchResult.NoMatch) result = RuleListMatchResult.WrongProtocol; continue; }
                if (!dirOk) { if (result == RuleListMatchResult.NoMatch) result = RuleListMatchResult.WrongDirection; continue; }
                if (!localPortOk || !remotePortOk) { if (result == RuleListMatchResult.NoMatch) result = RuleListMatchResult.WrongPort; continue; }

                return rule.Action == RuleAction.Allow
                    ? RuleListMatchResult.Allow
                    : RuleListMatchResult.Block;
            }
            return result;
        }

        private static bool PortMatches(string spec, int port)
        {
            if (string.IsNullOrEmpty(spec) || spec == "*") return true;
            foreach (var token in spec.Split(','))
            {
                var t = token.Trim();
                if (t.Length == 0) continue;
                int dash = t.IndexOf('-');
                if (dash > 0)
                {
                    if (int.TryParse(t.AsSpan(0, dash), out int lo) &&
                        int.TryParse(t.AsSpan(dash + 1), out int hi) &&
                        port >= lo && port <= hi)
                        return true;
                }
                else if (int.TryParse(t, out int p) && p == port)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsLocalNetworkRemote(string? remoteIp)
        {
            if (string.IsNullOrEmpty(remoteIp)) return false;
            // Best-effort: treat the usual private + loopback + link-local
            // ranges as "local network". The real enforcement lives in
            // WFP; this is post-hoc heuristic only.
            if (remoteIp.StartsWith("127.", StringComparison.Ordinal)) return true;
            if (remoteIp == "::1") return true;
            if (remoteIp.StartsWith("10.", StringComparison.Ordinal)) return true;
            if (remoteIp.StartsWith("192.168.", StringComparison.Ordinal)) return true;
            if (remoteIp.StartsWith("169.254.", StringComparison.Ordinal)) return true;
            if (remoteIp.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase)) return true;
            if (remoteIp.StartsWith("172.", StringComparison.Ordinal)
                && remoteIp.IndexOf('.', 4) is int dot && dot > 4
                && int.TryParse(remoteIp.AsSpan(4, dot - 4), out int second)
                && second >= 16 && second <= 31)
                return true;
            return false;
        }

        // ----- Near-misses -----------------------------------------------

        private static IReadOnlyList<NearMiss> BuildNearMisses(IReadOnlyList<FirewallExceptionV3> subjectMatches, FirewallEventRecord record)
        {
            var misses = new List<NearMiss>();
            foreach (var ex in subjectMatches)
            {
                string why = ExplainPolicyMismatch(ex, record);
                misses.Add(new NearMiss(ex.Id.ToString(), (ex.Subject.ToString() ?? "(unknown)"), why));
                if (misses.Count >= 3) break;
            }
            return misses;
        }

        private static IReadOnlyList<NearMiss> BuildNearMissesByApp(IReadOnlyList<FirewallExceptionV3> all, FirewallEventRecord record)
        {
            // Top 3 rules whose app name matches (best fuzzy match on exe
            // name) but whose path differs or policy rejects the flow.
            if (string.IsNullOrEmpty(record.AppName))
                return Array.Empty<NearMiss>();

            var misses = new List<(int score, NearMiss miss)>();
            foreach (var ex in all)
            {
                if (ex.Subject is ExecutableSubject exe)
                {
                    int score = 0;
                    if (string.Equals(exe.ExecutableName, record.AppName, StringComparison.OrdinalIgnoreCase))
                        score += 5;
                    else if (!string.IsNullOrEmpty(exe.ExecutableName) && !string.IsNullOrEmpty(record.AppName) &&
                             exe.ExecutableName.StartsWith(Path.GetFileNameWithoutExtension(record.AppName)!, StringComparison.OrdinalIgnoreCase))
                        score += 2;

                    if (score > 0)
                    {
                        string why = PathsEqual(exe.ExecutablePath, record.AppPath)
                            ? ExplainPolicyMismatch(ex, record)
                            : $"Rule subject path '{exe.ExecutablePath}' differs from event path '{record.AppPath}'.";
                        misses.Add((score, new NearMiss(ex.Id.ToString(), (ex.Subject.ToString() ?? "(unknown)"), why)));
                    }
                }
                else if (ex.Subject is GlobalSubject)
                {
                    misses.Add((1, new NearMiss(ex.Id.ToString(), (ex.Subject.ToString() ?? "(unknown)"), ExplainPolicyMismatch(ex, record))));
                }
            }

            return misses
                .OrderByDescending(m => m.score)
                .Take(3)
                .Select(m => m.miss)
                .ToList();
        }

        private static string ExplainPolicyMismatch(FirewallExceptionV3 ex, FirewallEventRecord record)
        {
            switch (ex.Policy.PolicyType)
            {
                case PolicyType.HardBlock:
                    return "Rule is a hard block for this subject.";
                case PolicyType.Unrestricted:
                    var upol = (UnrestrictedPolicy)ex.Policy;
                    if (upol.LocalNetworkOnly && !IsLocalNetworkRemote(record.RemoteIp))
                        return $"Rule allows unrestricted access but only on local network; remote {record.RemoteIp} is not local.";
                    return "Rule would allow this flow.";
                case PolicyType.TcpUdpOnly:
                    var tpol = (TcpUdpPolicy)ex.Policy;
                    var tres = MatchTcpUdpPolicy(tpol, record);
                    return tres switch
                    {
                        TcpUdpMatchResult.WrongProtocol =>
                            $"Rule covers TCP/UDP only; event was {record.Protocol}.",
                        TcpUdpMatchResult.WrongPort =>
                            DescribePortMismatch(tpol, record),
                        TcpUdpMatchResult.WrongDirection =>
                            $"Rule does not cover direction {record.Direction}.",
                        TcpUdpMatchResult.WrongLocalNetwork =>
                            $"Rule is local-network only; remote {record.RemoteIp} is not local.",
                        _ => "Rule would allow this flow.",
                    };
                case PolicyType.RuleList:
                    var rpol = (RuleListPolicy)ex.Policy;
                    var rres = MatchRuleList(rpol, record);
                    return rres switch
                    {
                        RuleListMatchResult.WrongProtocol => $"No rule in the list covers protocol {record.Protocol}.",
                        RuleListMatchResult.WrongDirection => $"No rule in the list covers direction {record.Direction}.",
                        RuleListMatchResult.WrongPort => $"No rule in the list covers port {record.RemotePort}/{record.LocalPort}.",
                        RuleListMatchResult.NoMatch => "No rule in the list matched.",
                        RuleListMatchResult.Block => "Rule list explicitly blocks this flow.",
                        _ => "Rule list would allow this flow.",
                    };
            }
            return "Policy did not match.";
        }

        private static string DescribePortMismatch(TcpUdpPolicy tpol, FirewallEventRecord r)
        {
            bool outbound = (r.Direction & RuleDirection.Out) != 0;
            string? allowed = outbound
                ? (r.Protocol == Protocol.TCP ? tpol.AllowedRemoteTcpConnectPorts : tpol.AllowedRemoteUdpConnectPorts)
                : (r.Protocol == Protocol.TCP ? tpol.AllowedLocalTcpListenerPorts : tpol.AllowedLocalUdpListenerPorts);

            int port = outbound ? r.RemotePort : r.LocalPort;
            return $"Port {port} not in allowed list [{allowed ?? "none"}] for {r.Protocol} {r.Direction}.";
        }

        // ----- Evidence chips --------------------------------------------

        private static IReadOnlyList<EvidenceChip> BuildEvidenceChips(FirewallEventRecord r, FirewallMode mode)
        {
            var chips = new List<EvidenceChip>(6);

            if (!string.IsNullOrEmpty(r.AppName))
                chips.Add(new EvidenceChip(EvidenceKind.App, r.AppName!, r.AppPath ?? r.AppName!, ChipSeverity.Neutral, FilterKey: "app_path"));

            if (!string.IsNullOrEmpty(r.RemoteIp))
                chips.Add(new EvidenceChip(EvidenceKind.Remote,
                    $"{r.RemoteIp}:{r.RemotePort}",
                    r.RemoteIp!,
                    ChipSeverity.Neutral,
                    FilterKey: "remote_ip"));

            chips.Add(new EvidenceChip(EvidenceKind.Protocol,
                r.Protocol.ToString(),
                ((int)r.Protocol).ToString(),
                ChipSeverity.Neutral,
                FilterKey: "protocol"));

            chips.Add(new EvidenceChip(EvidenceKind.Direction,
                r.Direction.ToString(),
                ((int)r.Direction).ToString(),
                ChipSeverity.Neutral,
                FilterKey: "direction"));

            chips.Add(new EvidenceChip(EvidenceKind.Mode,
                mode.ToString(),
                ((int)mode).ToString(),
                mode == FirewallMode.Disabled ? ChipSeverity.Warning : ChipSeverity.Neutral,
                FilterKey: "mode_at_event"));

            return chips;
        }

        // ----- Remediation -----------------------------------------------

        private static IReadOnlyList<RemediationAction> RemediationsForReason(ReasonId reason, FirewallEventRecord r)
        {
            var actions = new List<RemediationAction>(3);
            switch (reason)
            {
                case ReasonId.BlockedByModeBlockAll:
                case ReasonId.BlockedByModeDisabled:
                    actions.Add(new RemediationAction(RemediationKind.OpenRuleEditor, "Change firewall mode", "Switch the firewall out of this mode."));
                    break;

                case ReasonId.BlockedByMatchedBlockRule:
                    actions.Add(new RemediationAction(RemediationKind.OpenRuleEditor, "Edit blocking rule"));
                    actions.Add(new RemediationAction(RemediationKind.Dismiss, "Dismiss"));
                    break;

                case ReasonId.BlockedNoMatchInNormal:
                case ReasonId.BlockedRestrictedPorts:
                case ReasonId.BlockedWrongDirection:
                case ReasonId.BlockedRestrictedLocalNetwork:
                    actions.Add(new RemediationAction(RemediationKind.AllowOnce, "Allow once for this flow"));
                    actions.Add(new RemediationAction(RemediationKind.AllowAlways, "Allow always for this app",
                        Parameters: r.AppPath is null ? null
                            : new Dictionary<string, string> { ["app_path"] = r.AppPath }));
                    actions.Add(new RemediationAction(RemediationKind.BlockPermanently, "Block permanently"));
                    break;

                case ReasonId.AllowedByMatchedRule:
                case ReasonId.AllowedByOutgoingMode:
                case ReasonId.AllowedByLearningMode:
                case ReasonId.AllowedBySpecialException:
                    actions.Add(new RemediationAction(RemediationKind.BlockPermanently, "Block this app"));
                    actions.Add(new RemediationAction(RemediationKind.Dismiss, "Dismiss"));
                    break;
            }
            return actions;
        }

        private static Explanation BuildExplanation(
            ReasonId reason,
            Confidence confidence,
            string? matchedRuleId,
            string? matchedRuleDescription,
            IReadOnlyList<NearMiss> nearMisses,
            IReadOnlyList<EvidenceChip> evidence,
            IReadOnlyList<RemediationAction> remediations)
        {
            return new Explanation(
                reason,
                Explanation.ReasonTextKey(reason),
                confidence,
                matchedRuleId,
                matchedRuleDescription,
                nearMisses,
                evidence,
                remediations);
        }

        // ----- Snapshot cache --------------------------------------------

        private ServerConfiguration? LoadSnapshotConfig(long rulesetId)
        {
            lock (_cacheLock)
            {
                if (_snapshotCache.TryGetValue(rulesetId, out var cached))
                    return cached;
            }

            ServerConfiguration? config = null;
            try
            {
                var snapshot = _store.GetRulesetSnapshot(rulesetId);
                if (snapshot is not null && snapshot.ContentJson.Length > 0)
                {
                    config = SerializationHelper.Deserialize(snapshot.ContentJson, new ServerConfiguration());
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_SERVICE);
                config = null;
            }

            lock (_cacheLock)
            {
                _snapshotCache[rulesetId] = config;
                // Bound the cache — we don't expect many distinct rulesets
                // at once, but don't let a leak creep in.
                if (_snapshotCache.Count > 64)
                {
                    var first = _snapshotCache.Keys.First();
                    if (first != rulesetId) _snapshotCache.Remove(first);
                }
            }
            return config;
        }
    }
}
