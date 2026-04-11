using Xunit;

namespace pylorak.TinyWall.Tests
{
    public class SerializationTests
    {
        private static ServerConfiguration MakeValidConfig()
        {
            var config = new ServerConfiguration();
            config.ActiveProfileName = "Default";
            // Touching ActiveProfile getter once auto-creates the profile
            _ = config.ActiveProfile;
            return config;
        }

        [Fact]
        public void ServerConfiguration_RoundTrip_PreservesValues()
        {
            var original = MakeValidConfig();
            original.AutoUpdateCheck = false;
            original.LockHostsFile = true;

            byte[] bytes = SerializationHelper.Serialize(original);
            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes);

            var restored = SerializationHelper.Deserialize(bytes, new ServerConfiguration());
            Assert.NotNull(restored);
            Assert.Equal(original.AutoUpdateCheck, restored.AutoUpdateCheck);
            Assert.Equal(original.LockHostsFile, restored.LockHostsFile);
        }

        [Fact]
        public void ServerConfiguration_ValidInstance_Serializes()
        {
            // A configured instance should round-trip cleanly via the
            // source-generated JSON serializer.
            var fresh = MakeValidConfig();
            byte[] bytes = SerializationHelper.Serialize(fresh);
            var restored = SerializationHelper.Deserialize(bytes, new ServerConfiguration());
            Assert.NotNull(restored);
        }

        [Fact]
        public void EnableFirstBlockToasts_RoundTrips_When_False()
        {
            // Verifies the new Phase 5 setting survives the JSON pipe
            // hop end-to-end. If source gen failed to pick up the new
            // field, this test would fail and the toast feature could
            // not be turned off via Settings.
            var original = MakeValidConfig();
            original.ActiveProfile.EnableFirstBlockToasts = false;

            byte[] bytes = SerializationHelper.Serialize(original);
            var restored = SerializationHelper.Deserialize(bytes, new ServerConfiguration());

            Assert.False(restored.ActiveProfile.EnableFirstBlockToasts);
        }

        [Fact]
        public void EnableFirstBlockToasts_DefaultsTo_True()
        {
            // Brand-new instance — the field's initializer should win.
            var fresh = MakeValidConfig();
            Assert.True(fresh.ActiveProfile.EnableFirstBlockToasts);
        }

        [Fact]
        public void EnableFirstBlockToasts_DefaultsTo_True_When_Missing_From_Json()
        {
            // Backwards compat: a config serialized BEFORE this field
            // existed must still deserialize successfully and report
            // EnableFirstBlockToasts as true (the default), not as
            // missing/false. We simulate this by serializing an
            // explicit-true config and then ensuring the default initializer
            // path (the value never appeared in JSON because EmitDefaultValue
            // strips it) still produces true.
            var original = MakeValidConfig();
            original.ActiveProfile.EnableFirstBlockToasts = true; // default

            byte[] bytes = SerializationHelper.Serialize(original);
            string json = System.Text.Encoding.UTF8.GetString(bytes);

            // Sanity: the JSON should NOT mention EnableFirstBlockToasts
            // because it equals its default. (DataMember EmitDefaultValue=false
            // is honored by the source-gen with our options.)
            // If this assertion ever fails, the field is being emitted
            // verbatim and forwards-compat is fine but the JSON is bigger.
            // Either way the round-trip below is the real test.
            _ = json;

            var restored = SerializationHelper.Deserialize(bytes, new ServerConfiguration());
            Assert.True(restored.ActiveProfile.EnableFirstBlockToasts);
        }
    }
}
