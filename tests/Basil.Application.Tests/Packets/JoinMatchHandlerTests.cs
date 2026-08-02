using Basil.Application.Packets.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Ported from app/api/domains/cho.py's MatchJoin.</summary>
public class JoinMatchHandlerTests
{
	private static PacketReader ReaderFor(int matchId, string password)
	{
		byte[] body = [.. BinaryWriter.WriteInt32(matchId), .. BinaryWriter.WriteString(password)];
		return new PacketReader(body);
	}

	[Fact]
	public async Task Handle_UnknownMatch_SendsMatchJoinFail()
	{
		var fixture = new Fixture();
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);
		var player = MakePlayer(1, "alice");
		fixture.RegisterAll(player);

		await handler.HandleAsync(player, ReaderFor(0, ""));

		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(player.Dequeue()));
	}

	[Fact]
	public async Task Handle_Restricted_SendsMatchJoinFailWithoutTouchingMatch()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = fixture.CreateMatch(host);
		host.Dequeue();

		var guest = MakePlayer(2, "guest");
		guest.Privilege = 0;
		fixture.RegisterAll(host, guest);
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

		await handler.HandleAsync(guest, ReaderFor(match.Id, ""));

		Assert.Null(guest.Match);
		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Handle_CorrectPassword_JoinsMatch()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = (await fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id, password: "pw")))!;

		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

		await handler.HandleAsync(guest, ReaderFor(match.Id, "pw"));

		Assert.Same(match, guest.Match);
	}

	[Fact]
	public async Task Handle_PrivateMatch_CorrectPassword_UninvitedRejected()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = (await fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id, password: "pw")))!;
		match.IsPrivate = true;

		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

		await handler.HandleAsync(guest, ReaderFor(match.Id, "pw"));

		Assert.Null(guest.Match);
		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Handle_PrivateMatch_InvitedGuest_Succeeds()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match = (await fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id, password: "pw")))!;
		match.IsPrivate = true;

		var guest = MakePlayer(2, "guest");
		fixture.RegisterAll(host, guest);
		match.AddInvite(guest.Id);
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

		await handler.HandleAsync(guest, ReaderFor(match.Id, "pw"));

		Assert.Same(match, guest.Match);
	}

	[Fact]
	public async Task Handle_PrivateMatch_HostRejoinsWithoutInvite_Rejected()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match =
			(await fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id), true))!;
		match.IsPrivate = true;
		await fixture.MatchMembership.LeaveAsync(host, match);
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

		await handler.HandleAsync(host, ReaderFor(match.Id, ""));

		Assert.Null(host.Match);
		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(host.Dequeue()));
	}

	[Fact]
	public async Task Handle_PrivateMatch_HostRejoinsWithInvite_Succeeds()
	{
		var fixture = new Fixture();
		var host = MakePlayer(1, "host");
		fixture.RegisterAll(host);
		var match =
			(await fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id), true))!;
		match.IsPrivate = true;
		await fixture.MatchMembership.LeaveAsync(host, match);
		match.AddInvite(host.Id);
		var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

		await handler.HandleAsync(host, ReaderFor(match.Id, ""));

		Assert.Same(match, host.Match);
	}
}