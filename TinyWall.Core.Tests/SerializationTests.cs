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
    }
}
