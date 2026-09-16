using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Users;
using Basil.Application.Chat;
using Basil.Host.Bancho.Chat.Packets;
using Basil.Host.Bancho.Multiplayer;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Host.Bancho.Tests.Multiplayer.Packets;

/// <summary>Verifies the `TourneyMatchJoinChannel` handler joins an observing donator as a tournament client.</summary>
public class TourneyMatchJoinChannelHandlerTests
{
	private static PacketReader ReaderFor(int matchId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(matchId));
	}

	private static GameSession MakeDonator(int id, string name)
	{
		var player = MakePlayer(id, name);
		player.Privilege = UserPrivileges.Unrestricted | UserPrivileges.Supporter;
		return player;
	}

	[Fact]
	public async Task Handle_PlayingInTheMatch_DoesNotJoinAsTourneyClient()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		var handler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry,
			new ChannelMembershipService(fixture.SessionRegistry, fixture.IrcSessionRegistry, fixture.ChannelRegistry,
				new ChatNotifier(Options.Create(new IrcOptions())),
				new ChannelNotifier(fixture.SessionRegistry, fixture.IrcSessionRegistry,
					Options.Create(new IrcOptions())),
				Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions())),
			NullLogger<TourneyMatchJoinChannelHandler>.Instance);
		host.Privilege = UserPrivileges.Unrestricted | UserPrivileges.Supporter;

		await handler.HandleAsync(host, ReaderFor(match.Id));

		Assert.DoesNotContain(host.Id, match.TourneyClients);
	}

	[Fact]
	public async Task Handle_ObserverDonator_JoinsChannelAndBecomesTourneyClient()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var observer = MakeDonator(2, "observer");
		fixture.RegisterAll(host, observer);
		var match = fixture.CreateMatch(host);
		var handler = new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry,
			new ChannelMembershipService(fixture.SessionRegistry, fixture.IrcSessionRegistry, fixture.ChannelRegistry,
				new ChatNotifier(Options.Create(new IrcOptions())),
				new ChannelNotifier(fixture.SessionRegistry, fixture.IrcSessionRegistry,
					Options.Create(new IrcOptions())),
				Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions())),
			NullLogger<TourneyMatchJoinChannelHandler>.Instance);

		await handler.HandleAsync(observer, ReaderFor(match.Id));

		Assert.Contains(observer.Id, match.TourneyClients);
		Assert.True(observer.InChannel(match.ChatChannelName));
	}
}