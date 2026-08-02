using Basil.Application.Packets.Multiplayer;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchLeaveChannel.</summary>
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
			new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry),
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
		var membership = new ChannelMembershipService(fixture.SessionRegistry, fixture.ChannelRegistry);
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