using Basil.Application.Configurations;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Basil.Application.Tests.Packets.MultiplayerTestSupport;

namespace Basil.Application.Tests.Services.Spectating;

/// <summary>Verifies `SpectatorService`'s add/remove-spectator handling and the resulting channel membership.</summary>
public class SpectatorServiceTests
{
	private readonly FakeChannelRegistry _channelRegistry = new();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();

	private SpectatorService MakeService()
	{
		return new SpectatorService(_channelRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			NullLogger<SpectatorService>.Instance);
	}

	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private void RegisterAll(params GameSession[] sessions)
	{
		_gameRegistry.All.Returns(sessions);

		foreach (var session in sessions)
		{
			_gameRegistry.GetByUserId(session.Id).Returns(session);
			_gameRegistry.GetByUserId(session.Id).Returns(session);
		}
	}

	[Fact]
	public void AddSpectator_FirstSpectator_CreatesChannelAndNotifiesHost()
	{
		var host = MakePlayer(1, "host");
		var spectator = MakePlayer(2, "alice");
		RegisterAll(host, spectator);

		MakeService().AddSpectator(host, spectator);

		Assert.NotNull(_channelRegistry.GetByName("#spec_1"));
		Assert.Same(host, spectator.Spectating);
		Assert.Contains(spectator, host.Spectators);
		Assert.Contains(ServerPacketWriter.SpectatorJoined(spectator.Id), Chunk(host.Dequeue()));
	}

	[Fact]
	public void AddSpectator_SecondSpectator_NotifiesExistingAndNewSpectatorsOfEachOther()
	{
		var host = MakePlayer(1, "host");
		var first = MakePlayer(2, "alice");
		var second = MakePlayer(3, "bob");
		RegisterAll(host, first, second);
		MakeService().AddSpectator(host, first);
		host.Dequeue();
		first.Dequeue();

		MakeService().AddSpectator(host, second);

		Assert.Contains(ServerPacketWriter.FellowSpectatorJoined(second.Id), Chunk(first.Dequeue()));
		Assert.Contains(ServerPacketWriter.FellowSpectatorJoined(first.Id), Chunk(second.Dequeue()));
	}

	[Fact]
	public void AddSpectator_Stealth_DoesNotNotifyHostOrExistingSpectators()
	{
		var host = MakePlayer(1, "host");
		var first = MakePlayer(2, "alice");
		var stealthSpectator = MakePlayer(3, "admin");
		stealthSpectator.Stealth = true;
		RegisterAll(host, first, stealthSpectator);
		MakeService().AddSpectator(host, first);
		host.Dequeue();
		first.Dequeue();

		MakeService().AddSpectator(host, stealthSpectator);

		Assert.DoesNotContain(ServerPacketWriter.SpectatorJoined(stealthSpectator.Id), Chunk(host.Dequeue()));
		// `first` still gets the generic channel_info playercount update (channel membership
		// mechanics don't know about stealth) but never learns a fellow spectator joined.
		Assert.DoesNotContain(ServerPacketWriter.FellowSpectatorJoined(stealthSpectator.Id), Chunk(first.Dequeue()));
		Assert.Contains(ServerPacketWriter.FellowSpectatorJoined(first.Id), Chunk(stealthSpectator.Dequeue()));
	}

	[Fact]
	public void RemoveSpectator_LastSpectator_DeletesChannelAndHostLeaves()
	{
		var host = MakePlayer(1, "host");
		var spectator = MakePlayer(2, "alice");
		RegisterAll(host, spectator);
		MakeService().AddSpectator(host, spectator);

		MakeService().RemoveSpectator(host, spectator);

		Assert.Null(_channelRegistry.GetByName("#spec_1"));
		Assert.Null(spectator.Spectating);
		Assert.DoesNotContain(spectator, host.Spectators);
		Assert.False(host.InChannel("#spec_1"));
	}

	[Fact]
	public void RemoveSpectator_OthersRemain_ChannelSurvivesAndRemainingAreNotified()
	{
		var host = MakePlayer(1, "host");
		var first = MakePlayer(2, "alice");
		var second = MakePlayer(3, "bob");
		RegisterAll(host, first, second);
		var service = MakeService();
		service.AddSpectator(host, first);
		service.AddSpectator(host, second);
		first.Dequeue();

		service.RemoveSpectator(host, second);

		Assert.NotNull(_channelRegistry.GetByName("#spec_1"));
		Assert.Contains(ServerPacketWriter.FellowSpectatorLeft(second.Id), Chunk(first.Dequeue()));
	}

	private static List<byte[]> Chunk(byte[] data)
	{
		var chunks = new List<byte[]>();
		var offset = 0;
		while (offset < data.Length)
		{
			var length = BitConverter.ToInt32(data, offset + 3);
			var total = 7 + length;
			chunks.Add(data[offset..(offset + total)]);
			offset += total;
		}

		return chunks;
	}
}