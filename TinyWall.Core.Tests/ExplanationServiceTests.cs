using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using pylorak.TinyWall.History;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    public class ExplanationServiceTests
    {
        private const string TestAppPath = @"C:\Windows\System32\chrome.exe";
        private const string OtherAppPath = @"C:\Program Files\Other\other.exe";

        /// <summary>
        /// Builds a valid ServerConfiguration with a named active profile
        /// and optional exceptions. The tests use this to exercise the
        /// pure ExplainAgainst() core without touching a DB.
        /// </summary>
        private static ServerConfiguration CreateTestServerConfiguration(params FirewallExceptionV3[] exceptions)
        {
            var config = new ServerConfiguration();
            config.ActiveProfileName = "Test";
            var profile = config.ActiveProfile;
            profile.AppExceptions.AddRange(exceptions);
            return config;
        }

        private static FirewallEventRecord BlockedOutboundEvent(
            Protocol protocol = Protocol.TCP,
            int remotePort = 443,
            string appPath = TestAppPath,
            string? remoteIp = "1.2.3.4")
        {
            return new FirewallEventRecord
            {
                Id = 42,
                Direction = RuleDirection.Out,
                Protocol = protocol,
                LocalIp = "192.168.1.10",
                LocalPort = 50000,
                RemoteIp = remoteIp,
                RemotePort = remotePort,
                AppPath = appPath,
                AppName = System.IO.Path.GetFileName(appPath),
                Action = EventAction.Block,
                ModeAtEvent = FirewallMode.Normal,
                TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }

        private static ExplanationService NewService() => new(new StubEventStore());

        [Fact]
        public void Blocked_in_BlockAllMode_has_BlockedByMode_reason()
        {
            var record = BlockedOutboundEvent();
            record.ModeAtEvent = FirewallMode.BlockAll;
            var config = CreateTestServerConfiguration();

            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.BlockAll);

            Assert.Equal(ReasonId.BlockedByModeBlockAll, result.PrimaryReason);
            Assert.Equal(Confidence.Medium, result.Confidence);
            Assert.Empty(result.NearMisses);
            Assert.NotEmpty(result.Evidence);
        }

        [Fact]
        public void Blocked_with_no_matching_rule_in_Normal_has_NoMatchInNormalMode()
        {
            var record = BlockedOutboundEvent();
            // Config has an exception for a *different* exe.
            var otherEx = new FirewallExceptionV3(
                new ExecutableSubject(OtherAppPath),
                new UnrestrictedPolicy());
            var config = CreateTestServerConfiguration(otherEx);

            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.Normal);

            Assert.Equal(ReasonId.BlockedNoMatchInNormal, result.PrimaryReason);
            // There are remediation buttons offered for "add allow rule".
            Assert.Contains(result.Remediations, r => r.Kind == RemediationKind.AllowAlways);
        }

        [Fact]
        public void Blocked_but_rule_wrong_protocol_produces_MatchedRestrictedPorts_with_near_miss()
        {
            // The exception is a TCP-only allow for chrome.exe outbound.
            var tcpPolicy = new TcpUdpPolicy
            {
                AllowedRemoteTcpConnectPorts = "443",
            };
            var ex = new FirewallExceptionV3(
                new ExecutableSubject(TestAppPath),
                tcpPolicy);
            var config = CreateTestServerConfiguration(ex);

            // Event is UDP on port 53 — wrong port for the policy.
            var record = BlockedOutboundEvent(protocol: Protocol.TCP, remotePort: 53);

            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.Normal);

            Assert.Equal(ReasonId.BlockedRestrictedPorts, result.PrimaryReason);
            Assert.NotEmpty(result.NearMisses);
            Assert.Equal(ex.Id.ToString(), result.NearMisses[0].RuleId);
            Assert.Contains("53", result.NearMisses[0].WhyItDidntMatch);
        }

        [Fact]
        public void Allowed_event_with_matching_rule_has_MatchedAllowRule()
        {
            var ex = new FirewallExceptionV3(
                new ExecutableSubject(TestAppPath),
                new UnrestrictedPolicy());
            var config = CreateTestServerConfiguration(ex);

            var record = BlockedOutboundEvent();
            record.Action = EventAction.Allow;

            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.Normal);

            Assert.Equal(ReasonId.AllowedByMatchedRule, result.PrimaryReason);
            Assert.Equal(ex.Id.ToString(), result.MatchedRuleId);
            Assert.Contains(result.Evidence, e => e.Kind == EvidenceKind.Rule);
        }

        [Fact]
        public void Explain_with_missing_ruleset_snapshot_returns_Unknown_with_LowConfidence()
        {
            // Store returns no snapshot + no event for id 999.
            var store = new StubEventStore();
            var svc = new ExplanationService(store);

            var result = svc.Explain(decisionId: 999);

            Assert.Equal(ReasonId.Unknown, result.PrimaryReason);
            Assert.Equal(Confidence.Low, result.Confidence);
        }

        [Fact]
        public void Explain_with_event_but_missing_snapshot_returns_Unknown_LowConfidence()
        {
            var store = new StubEventStore();
            store.Events[7] = new FirewallEventRecord
            {
                Id = 7,
                RulesetId = 1234, // nothing stored under this id
                Action = EventAction.Block,
                ModeAtEvent = FirewallMode.Normal,
                Direction = RuleDirection.Out,
                Protocol = Protocol.TCP,
                AppPath = TestAppPath,
                AppName = "chrome.exe",
            };
            var svc = new ExplanationService(store);

            var result = svc.Explain(7);

            Assert.Equal(ReasonId.Unknown, result.PrimaryReason);
            Assert.Equal(Confidence.Low, result.Confidence);
        }

        [Fact]
        public void NearMiss_identifies_closest_3_rules_by_subject_match()
        {
            // Five exceptions. Three are for chrome.exe at different paths
            // or with mismatching port policies; two are for unrelated
            // apps. The engine should return the 3 chrome-related ones.
            var config = CreateTestServerConfiguration(
                new FirewallExceptionV3(new ExecutableSubject(@"C:\Other1\chrome.exe"), new UnrestrictedPolicy()),
                new FirewallExceptionV3(new ExecutableSubject(@"C:\Other2\chrome.exe"), new UnrestrictedPolicy()),
                new FirewallExceptionV3(new ExecutableSubject(@"C:\Other3\chrome.exe"), new UnrestrictedPolicy()),
                new FirewallExceptionV3(new ExecutableSubject(@"C:\Nope\firefox.exe"), new UnrestrictedPolicy()),
                new FirewallExceptionV3(new ExecutableSubject(@"C:\Nope\edge.exe"), new UnrestrictedPolicy())
            );

            var record = BlockedOutboundEvent();
            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.Normal);

            Assert.Equal(ReasonId.BlockedNoMatchInNormal, result.PrimaryReason);
            // Top 3 near-misses should all be chrome.exe (by filename match),
            // not firefox or edge.
            Assert.Equal(3, result.NearMisses.Count);
            foreach (var nm in result.NearMisses)
                Assert.Contains("chrome.exe", nm.RuleDescription, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void HardBlock_exception_on_matching_subject_yields_BlockedByMatchedBlockRule()
        {
            var ex = new FirewallExceptionV3(
                new ExecutableSubject(TestAppPath),
                HardBlockPolicy.Instance);
            var config = CreateTestServerConfiguration(ex);

            var record = BlockedOutboundEvent();
            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.Normal);

            Assert.Equal(ReasonId.BlockedByMatchedBlockRule, result.PrimaryReason);
            Assert.Equal(ex.Id.ToString(), result.MatchedRuleId);
            Assert.NotEmpty(result.NearMisses);
        }

        [Fact]
        public void TcpUdp_allow_matching_port_gives_AllowedByMatchedRule()
        {
            var policy = new TcpUdpPolicy { AllowedRemoteTcpConnectPorts = "443,80" };
            var ex = new FirewallExceptionV3(new ExecutableSubject(TestAppPath), policy);
            var config = CreateTestServerConfiguration(ex);

            var record = BlockedOutboundEvent(protocol: Protocol.TCP, remotePort: 443);
            record.Action = EventAction.Allow;
            var result = ExplanationService.ExplainAgainst(record, config, FirewallMode.Normal);

            Assert.Equal(ReasonId.AllowedByMatchedRule, result.PrimaryReason);
        }

        /// <summary>
        /// Minimal in-memory IFirewallEventStore stand-in so tests don't
        /// need SQLite. Only Explain()/ExplainAgainst() paths use this;
        /// the capture path and maintenance are no-ops here.
        /// </summary>
        private sealed class StubEventStore : IFirewallEventStore
        {
            public Dictionary<long, FirewallEventRecord> Events { get; } = new();
            public Dictionary<long, RulesetSnapshot> Snapshots { get; } = new();
            public long EventsDropped => 0;
            public bool Enabled { get; set; } = true;

            public void Dispose() { }

            public void Enqueue(FirewallLogEntry entry, FirewallMode modeAtEvent, long rulesetId) { }

            public long GetOrCreateRulesetSnapshot(byte[] canonicalJson) => 1;

            public void RunMaintenance() { }

            public FirewallEventRecord? GetEventById(long id) =>
                Events.TryGetValue(id, out var r) ? r : null;

            public IReadOnlyList<FirewallEventRecord> GetUnexplainedBatch(int limit) =>
                Events.Values.Where(e => e.ReasonId == ReasonId.Unknown).Take(limit).ToList();

            public void UpdateReason(long eventId, ReasonId reason, Confidence confidence, string? matchedRuleId, string? nearMissRuleIds)
            {
                if (Events.TryGetValue(eventId, out var e))
                {
                    e.ReasonId = reason;
                    e.Confidence = confidence;
                    e.MatchedRuleId = matchedRuleId;
                    e.NearMissRuleIds = nearMissRuleIds;
                }
            }

            public int UpdateReasons(IReadOnlyList<ReasonUpdate> updates)
            {
                int n = 0;
                foreach (var u in updates)
                {
                    if (Events.TryGetValue(u.EventId, out var e))
                    {
                        e.ReasonId = u.Reason;
                        e.Confidence = u.Confidence;
                        e.MatchedRuleId = u.MatchedRuleId;
                        e.NearMissRuleIds = u.NearMissRuleIds;
                        n++;
                    }
                }
                return n;
            }

            public RulesetSnapshot? GetRulesetSnapshot(long id) =>
                Snapshots.TryGetValue(id, out var s) ? s : null;
        }

        [Fact]
        public void Backfill_updates_unexplained_events_in_store()
        {
            var store = new StubEventStore();
            var ex = new FirewallExceptionV3(
                new ExecutableSubject(TestAppPath),
                HardBlockPolicy.Instance);
            var config = CreateTestServerConfiguration(ex);

            // Put a ruleset snapshot into the stub.
            var json = SerializationHelper.Serialize(config);
            store.Snapshots[1] = new RulesetSnapshot
            {
                Id = 1,
                TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ContentHash = "h",
                ContentJson = json,
            };

            // And an event that references it.
            var rec = BlockedOutboundEvent();
            rec.Id = 42;
            rec.RulesetId = 1;
            store.Events[42] = rec;

            var svc = new ExplanationService(store);
            svc.Backfill(CancellationToken.None);

            Assert.Equal(ReasonId.BlockedByMatchedBlockRule, store.Events[42].ReasonId);
            Assert.Equal(ex.Id.ToString(), store.Events[42].MatchedRuleId);
        }
    }
}
