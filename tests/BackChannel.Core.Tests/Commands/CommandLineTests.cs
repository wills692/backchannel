using BackChannel.App.Commands;
using Xunit;

namespace BackChannel.Core.Tests.Commands;

public sealed class CommandLineTests
{
    [Fact]
    public void ParseReturnsChatTextForOrdinaryInput()
    {
        var result = CommandLine.Parse("  hello there  ");

        Assert.False(result.IsCommand);
        Assert.Equal("hello there", result.Text);
        Assert.Empty(result.Name);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void ParseNormalizesCommandAndSeparatesArguments()
    {
        var result = CommandLine.Parse(" /MsG Alice hello there ");

        Assert.True(result.IsCommand);
        Assert.Equal("msg", result.Name);
        Assert.Equal("Alice hello there", result.Arguments);
    }

    [Fact]
    public void ParseSupportsCommandWithoutArguments()
    {
        var result = CommandLine.Parse("/peers");

        Assert.True(result.IsCommand);
        Assert.Equal("peers", result.Name);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void ParseTreatsSlashOnlyAsAnEmptyCommand()
    {
        var result = CommandLine.Parse("/");

        Assert.True(result.IsCommand);
        Assert.Empty(result.Name);
        Assert.Empty(result.Arguments);
    }
}
