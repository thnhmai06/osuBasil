using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions.Social;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Mail;
using OpenOsuTournament.Bancho.Domain.Users;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Mail;

/// <summary>
///     Ported from app/services/mail.py's MailReadService.mark_channel_as_read. "channel" is the mail
///     sender's player name (a naming quirk from the Python source), resolved online-first then via DB.
/// </summary>
public class MailReadServiceTests
{
    private readonly IMailRepository _mail = Substitute.For<IMailRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
    }

    [Fact]
    public async Task MarkChannelAsReadAsync_EmptyChannel_NoOp()
    {
        var service = new MailReadService(_sessionRegistry, _users, _mail);

        await service.MarkChannelAsReadAsync(MakePlayer(1, "cmyui"), "");

        await _mail.DidNotReceiveWithAnyArgs().MarkConversationAsReadAsync(default, default);
    }

    [Fact]
    public async Task MarkChannelAsReadAsync_SenderOnline_MarksReadUsingSessionId()
    {
        var player = MakePlayer(1, "cmyui");
        var sender = MakePlayer(2, "other");
        _sessionRegistry.GetByName("other").Returns(sender);
        var service = new MailReadService(_sessionRegistry, _users, _mail);

        await service.MarkChannelAsReadAsync(player, "other");

        await _mail.Received(1).MarkConversationAsReadAsync(1, 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkChannelAsReadAsync_SenderOffline_FallsBackToDbLookup()
    {
        var player = MakePlayer(1, "cmyui");
        _sessionRegistry.GetByName("offlineuser").Returns((PlayerSession?)null);
        _users.FetchByNameAsync("offlineuser", Arg.Any<CancellationToken>()).Returns(new User(
            5, "offlineuser", "offlineuser", null, 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, null));
        var service = new MailReadService(_sessionRegistry, _users, _mail);

        await service.MarkChannelAsReadAsync(player, "offlineuser");

        await _mail.Received(1).MarkConversationAsReadAsync(1, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkChannelAsReadAsync_SenderNotFoundAnywhere_NoOp()
    {
        var player = MakePlayer(1, "cmyui");
        _sessionRegistry.GetByName("nobody").Returns((PlayerSession?)null);
        _users.FetchByNameAsync("nobody", Arg.Any<CancellationToken>()).Returns((User?)null);
        var service = new MailReadService(_sessionRegistry, _users, _mail);

        await service.MarkChannelAsReadAsync(player, "nobody");

        await _mail.DidNotReceiveWithAnyArgs().MarkConversationAsReadAsync(default, default);
    }
}