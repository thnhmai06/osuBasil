using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's roll.</summary>
public class RollCommandTests
{
    private static CommandContext MakeContext(PlayerSession player, params string[] args) =>
        new(player, args, null, null);

    [Fact]
    public async Task HandleAsync_NoArgs_RollsUpToOneHundred()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await new RollCommand().HandleAsync(MakeContext(player));

        Assert.Matches(@"^cmyui rolls \d+ points!$", response!);
        var points = int.Parse(response!.Split(' ')[2]);
        Assert.InRange(points, 0, 99);
    }

    [Fact]
    public async Task HandleAsync_WithMaxArg_RollsUpToGivenMax()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await new RollCommand().HandleAsync(MakeContext(player, "5"));

        var points = int.Parse(response!.Split(' ')[2]);
        Assert.InRange(points, 0, 4);
    }

    [Fact]
    public async Task HandleAsync_MaxArgZero_ReturnsRollWhat()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await new RollCommand().HandleAsync(MakeContext(player, "0"));

        Assert.Equal("Roll what?", response);
    }

    [Fact]
    public async Task HandleAsync_MaxArgAboveCeiling_ClampsTo0x7FFF()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await new RollCommand().HandleAsync(MakeContext(player, "999999"));

        var points = int.Parse(response!.Split(' ')[2]);
        Assert.InRange(points, 0, 0x7FFF - 1);
    }

    [Fact]
    public async Task HandleAsync_NonNumericArg_FallsBackToOneHundred()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await new RollCommand().HandleAsync(MakeContext(player, "abc"));

        var points = int.Parse(response!.Split(' ')[2]);
        Assert.InRange(points, 0, 99);
    }
}
