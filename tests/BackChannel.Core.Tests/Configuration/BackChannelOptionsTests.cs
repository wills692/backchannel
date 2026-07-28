using System.Net;
using BackChannel.App.Configuration;
using Xunit;

namespace BackChannel.Core.Tests.Configuration;

public sealed class BackChannelOptionsTests
{
    [Fact]
    public void DefaultsAreValidAndUseBroadcastDiscovery()
    {
        var options = new BackChannelOptions();

        Assert.True(BackChannelOptions.IsValid(options));
        Assert.True(
            BackChannelOptions.TryGetDiscoveryTarget(
                options,
                out var endpoint));
        Assert.Null(endpoint);
    }

    [Fact]
    public void ExplicitIpv4DiscoveryTargetIsParsed()
    {
        var options = new BackChannelOptions
        {
            DiscoveryTarget = "127.0.0.1:52521",
        };

        Assert.True(BackChannelOptions.IsValid(options));
        Assert.True(
            BackChannelOptions.TryGetDiscoveryTarget(
                options,
                out var endpoint));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 52521), endpoint);
    }

    [Theory]
    [InlineData("not-an-endpoint")]
    [InlineData("127.0.0.1:0")]
    [InlineData("[::1]:52520")]
    public void InvalidDiscoveryTargetFailsValidation(string target)
    {
        var options = new BackChannelOptions
        {
            DiscoveryTarget = target,
        };

        Assert.False(BackChannelOptions.IsValid(options));
    }
}
