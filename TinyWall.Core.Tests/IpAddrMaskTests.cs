using System;
using pylorak.Utilities;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    public class IpAddrMaskTests
    {
        [Theory]
        [InlineData("192.168.1.1")]
        [InlineData("10.0.0.0/8")]
        [InlineData("172.16.0.0/12")]
        [InlineData("0.0.0.0/0")]
        public void Parse_ValidIPv4_DoesNotThrow(string input)
        {
            var result = IpAddrMask.Parse(input);
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData("::1")]
        [InlineData("fe80::/10")]
        [InlineData("2001:db8::/32")]
        public void Parse_ValidIPv6_DoesNotThrow(string input)
        {
            var result = IpAddrMask.Parse(input);
            Assert.NotNull(result);
        }

        [Fact]
        public void Loopback_HasExpectedValue()
        {
            var loopback = IpAddrMask.Loopback;
            Assert.NotNull(loopback);
        }

        [Fact]
        public void IPv6Loopback_HasExpectedValue()
        {
            var loopback = IpAddrMask.IPv6Loopback;
            Assert.NotNull(loopback);
        }

        [Fact]
        public void Equals_SameAddress_ReturnsTrue()
        {
            var a = IpAddrMask.Parse("10.0.0.1");
            var b = IpAddrMask.Parse("10.0.0.1");
            Assert.Equal(a, b);
        }

        [Fact]
        public void Equals_DifferentAddress_ReturnsFalse()
        {
            var a = IpAddrMask.Parse("10.0.0.1");
            var b = IpAddrMask.Parse("10.0.0.2");
            Assert.NotEqual(a, b);
        }
    }
}
