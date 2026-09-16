using Basil.Application.Multiplayer;
using Basil.Application.Shared.Configuration;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat;
using Basil.Infrastructure.Chat.Packets;
using Basil.Infrastructure.Multiplayer;
using Basil.Infrastructure.Multiplayer.Packets;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Basil.Infrastructure.Tests.Multiplayer.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Infrastructure.Tests.Multiplayer.Packets;

/// <summary>Verifies the `TourneyMatchLeaveChannel` handler parts the tournament client from the match channel.</summary>
public class TourneyMatchLeaveChannelHandlerTests
{
	private static PacketReader ReaderFor(int matchId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(matchId));
	}

	[Fact]
	public async Task Handle_NotATourneyClient_NoOp()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var observer = MakePlayer(2, "observer");
		observer.Privilege = UserPrivileges.Unrestricted | UserPrivileges.Supporter;
		fixture.RegisterAll(host, observer);
		var match = fixture.CreateMatch(host);
		var handler = new TourneyMatchLeaveChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry,
			new ChannelMembershipService(fixture.SessionRegistry, fixture.IrcSessionRegistry, fixture.ChannelRegistry, new ChatNotifier(Options.Create(new IrcOptions())), new ChannelNotifier(fixture.SessionRegistry,fixture.IrcSessionRegistry, Options.Create(new IrcOptions())),
				Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions())),
			NullLogger<TourneyMatchLeaveChannelHandler>.Instance);

		await handler.HandleAsync(observer, ReaderFor(match.Id));

		Assert.False(observer.InChannel(match.ChatChannelName));
	}

	[Fact]
	public async Task Handle_TourneyClient_LeavesChannelAndIsRemoved()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		var observer = MakePlayer(2, "observer");
		observer.Privilege = UserPrivileges.Unrestricted | UserPrivileges.Supporter;
		fixture.RegisterAll(host, observer);
		var match = fixture.CreateMatch(host);
		var membership = new ChannelMembershipService(fixture.SessionRegistry, fixture.IrcSessionRegistry,
			fixture.ChannelRegistry, new ChatNotifier(Options.Create(new IrcOptions())), new ChannelNotifier(fixture.SessionRegistry,fixture.IrcSessionRegistry, Options.Create(new IrcOptions())), Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(),
			Options.Create(new IrcOptions()));
		var joinHandler =
			new TourneyMatchJoinChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, membership,
				NullLogger<TourneyMatchJoinChannelHandler>.Instance);
		await joinHandler.HandleAsync(observer, ReaderFor(match.Id));
		var handler = new TourneyMatchLeaveChannelHandler(fixture.MatchRegistry, fixture.ChannelRegistry, membership,
			NullLogger<TourneyMatchLeaveChannelHandler>.Instance);

		await handler.HandleAsync(observer, ReaderFor(match.Id));

		Assert.DoesNotContain(observer.Id, match.TourneyClients);
		Assert.False(observer.InChannel(match.ChatChannelName));
	}
}