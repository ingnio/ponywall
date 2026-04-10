using System.IO;
using System.Text;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    public class HasherTests
    {
        [Fact]
        public void HashString_EmptyString_ReturnsKnownSha256()
        {
            // SHA-256 of empty string is well known
            var hash = Hasher.HashString(string.Empty);
            Assert.Equal(
                "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                hash);
        }

        [Fact]
        public void HashString_KnownInput_ReturnsExpectedSha256()
        {
            // SHA-256 of "abc" is the canonical FIPS test vector
            var hash = Hasher.HashString("abc");
            Assert.Equal(
                "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                hash);
        }

        [Fact]
        public void HashString_SameInputTwice_ReturnsSameHash()
        {
            var a = Hasher.HashString("test input");
            var b = Hasher.HashString("test input");
            Assert.Equal(a, b);
        }

        [Fact]
        public void HashString_DifferentInputs_ReturnsDifferentHashes()
        {
            var a = Hasher.HashString("foo");
            var b = Hasher.HashString("bar");
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void HashStream_MatchesHashString()
        {
            var bytes = Encoding.UTF8.GetBytes("test");
            using var ms = new MemoryStream(bytes);
            var streamHash = Hasher.HashStream(ms);
            var stringHash = Hasher.HashString("test");
            Assert.Equal(stringHash, streamHash);
        }

        [Fact]
        public void HashFile_RoundTrip_MatchesContent()
        {
            var temp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(temp, "hello");
                var fileHash = Hasher.HashFile(temp);
                var stringHash = Hasher.HashString("hello");
                Assert.Equal(stringHash, fileHash);
            }
            finally
            {
                File.Delete(temp);
            }
        }
    }
}
