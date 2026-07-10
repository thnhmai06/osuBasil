using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Anticheat;
using Basil.Domain;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Anticheat;

/// <summary>
///     Ported from app/services/client_integrity.py's ClientIntegrityService.handle_lastfm_flags.
///     Per explicit user decision, restrict/force-logout/random-ban-roll/Discord-webhook side effects
///     are dropped entirely — detected flags are only logged for manual review (no restrict machinery
///     exists in Basil).
/// </summary>
public class ClientIntegrityServiceTests
{
    private readonly ILogRepository _logs = Substitute.For<ILogRepository>();

    private static PlayerSession MakePlayer()
    {
        return new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_NotAnticheatFlag_ReturnsStopSendingWithoutLogging()
    {
        var service = new ClientIntegrityService(_logs);

        var result = await service.HandleLastFmFlagsAsync(MakePlayer(), "12345");

        Assert.Equal(ClientIntegrityResult.StopSending, result);
        await _logs.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!, null!);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_EmptyFlag_ReturnsStopSending()
    {
        var service = new ClientIntegrityService(_logs);

        var result = await service.HandleLastFmFlagsAsync(MakePlayer(), "");

        Assert.Equal(ClientIntegrityResult.StopSending, result);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_HqOsuFlag_LogsAndReturnsStopSending()
    {
        var player = MakePlayer();
        var service = new ClientIntegrityService(_logs);
        var flag = $"a{(int)LastFmFlags.HqAssembly}";

        var result = await service.HandleLastFmFlagsAsync(player, flag);

        Assert.Equal(ClientIntegrityResult.StopSending, result);
        await _logs.Received(1).CreateAsync(0, player.Id, "lastfm_flag", Arg.Is<string>(m => m.Contains("hq!osu")));
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_RegistryEditsFlag_LogsAndReturnsStopSending()
    {
        var player = MakePlayer();
        var service = new ClientIntegrityService(_logs);
        var flag = $"a{(int)LastFmFlags.RegistryEdits}";

        var result = await service.HandleLastFmFlagsAsync(player, flag);

        Assert.Equal(ClientIntegrityResult.StopSending, result);
        await _logs.Received(1).CreateAsync(0, player.Id, "lastfm_flag", Arg.Any<string>());
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_NoSuspiciousFlag_ReturnsEmptyWithoutLogging()
    {
        var service = new ClientIntegrityService(_logs);
        var flag = $"a{(int)LastFmFlags.ConsoleOpen}";

        var result = await service.HandleLastFmFlagsAsync(MakePlayer(), flag);

        Assert.Equal(ClientIntegrityResult.Empty, result);
        await _logs.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, null!, null!);
    }
}