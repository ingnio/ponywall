using System;
using System.IO;
using pylorak.TinyWall.History;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    /// <summary>
    /// Exercises the persistent first-block toast deduper. Each test
    /// uses a unique temp file path so the suite can run in parallel.
    /// </summary>
    public class ToastDeduperTests : IDisposable
    {
        private readonly string _path;

        public ToastDeduperTests()
        {
            _path = Path.Combine(Path.GetTempPath(),
                "tw_toastdedupe_" + Guid.NewGuid().ToString("N") + ".json");
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
        }

        [Fact]
        public void Should_toast_first_time_then_suppress_within_cooldown()
        {
            var d = new ToastDeduper(_path) { CooldownMinutes = 5 };

            long t0 = 1_000_000;
            Assert.True(d.ShouldToast(@"C:\app\foo.exe", t0));
            d.MarkToasted(@"C:\app\foo.exe", t0);

            // Same app, 1 minute later — within 5 min cooldown.
            Assert.False(d.ShouldToast(@"C:\app\foo.exe", t0 + 60_000));

            // Same app, 6 minutes later — past cooldown, should fire again.
            Assert.True(d.ShouldToast(@"C:\app\foo.exe", t0 + 6 * 60_000));
        }

        [Fact]
        public void Should_reject_null_empty_and_noisy_system_apps()
        {
            var d = new ToastDeduper(_path);
            long now = 1_000_000;

            Assert.False(d.ShouldToast(null, now));
            Assert.False(d.ShouldToast(string.Empty, now));
            Assert.False(d.ShouldToast("System", now));
            Assert.False(d.ShouldToast(@"C:\Windows\System32\svchost.exe", now));
            Assert.False(d.ShouldToast("svchost.exe", now));
        }

        [Fact]
        public void Different_apps_are_independently_throttled()
        {
            var d = new ToastDeduper(_path) { CooldownMinutes = 5 };

            long t = 1_000_000;
            Assert.True(d.ShouldToast(@"C:\a.exe", t));
            d.MarkToasted(@"C:\a.exe", t);

            Assert.True(d.ShouldToast(@"C:\b.exe", t));
            d.MarkToasted(@"C:\b.exe", t);

            Assert.False(d.ShouldToast(@"C:\a.exe", t));
            Assert.False(d.ShouldToast(@"C:\b.exe", t));
            Assert.True(d.ShouldToast(@"C:\c.exe", t));
        }

        [Fact]
        public void App_path_match_is_case_insensitive()
        {
            var d = new ToastDeduper(_path);
            long t = 1_000_000;
            d.MarkToasted(@"C:\App\Foo.exe", t);
            Assert.False(d.ShouldToast(@"c:\app\foo.exe", t));
            Assert.False(d.ShouldToast(@"C:\APP\FOO.EXE", t));
        }

        [Fact]
        public void Save_and_reload_round_trips_state_through_disk()
        {
            long t = 1_000_000;
            {
                var d = new ToastDeduper(_path);
                d.MarkToasted(@"C:\foo.exe", t);
                d.MarkToasted(@"C:\bar.exe", t + 1000);
                d.Save();
            }

            // Reopen — the cooldown state should survive.
            var reopened = new ToastDeduper(_path) { CooldownMinutes = 5 };
            Assert.Equal(2, reopened.Count);
            Assert.False(reopened.ShouldToast(@"C:\foo.exe", t + 60_000));
            Assert.False(reopened.ShouldToast(@"C:\bar.exe", t + 60_000));
        }

        [Fact]
        public void Save_is_a_noop_when_nothing_has_changed()
        {
            var d = new ToastDeduper(_path);
            d.Save();
            // Nothing was marked yet, so no file should exist.
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void Marking_after_load_persists_on_next_save()
        {
            long t = 1_000_000;
            var d = new ToastDeduper(_path);
            d.MarkToasted(@"C:\new.exe", t);
            d.Save();
            Assert.True(File.Exists(_path));

            var reopened = new ToastDeduper(_path);
            Assert.Equal(1, reopened.Count);
        }
    }
}
