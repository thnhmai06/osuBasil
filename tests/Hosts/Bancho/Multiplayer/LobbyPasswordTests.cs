using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Multiplayer;

/// <summary>A room's join password is shown to the people inside it, never to the lobby's watchers.</summary>
[Collection("Bancho host")]
public sealed class LobbyPasswordTests(BanchoHostFixture fixture)
{
	private const string Secret = "lobby-secret";

	private BasilHost Host => fixture.Host;

	/// <summary>Reads the password field of a match payload: id, in-progress, type, mods, name, then the password.</summary>
	private static string PasswordOf(ServerPacket packet)
	{
		var position = 8;
		packet.ReadString(ref position);
		return packet.ReadString(ref position);
	}

	[Fact]
	public async Task NewRoomWithPassword_ShownToALobbyWatcher_CarriesABlankPassword()
	{
		var watcher = await Host.NewClientAsync("PwLiveWatcher");
		var creator = await Host.NewClientAsync("PwLiveCreator");
		await watcher.LoginAsync();
		await creator.LoginAsync();
		await watcher.SendAsync(writer => writer.Packet(OsuClient.Out.JoinLobby));

		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("pw live room", Secret)));

		var announced = await watcher.WaitForAsync(OsuClient.In.NewMatch);
		Assert.NotNull(announced);
		Assert.Equal(string.Empty, PasswordOf(announced));
	}

	[Fact]
	public async Task NewRoomWithPassword_JoinSuccessForTheCreator_CarriesTheRealPassword()
	{
		var creator = await Host.NewClientAsync("PwOwnCreator");
		await creator.LoginAsync();

		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("pw own room", Secret)));

		var joined = await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess);
		Assert.NotNull(joined);
		Assert.Equal(Secret, PasswordOf(joined));
	}

	[Fact]
	public async Task ExistingRoomWithPassword_ListedToAWatcherWhoJoinsLater_CarriesABlankPassword()
	{
		var creator = await Host.NewClientAsync("PwListCreator");
		var watcher = await Host.NewClientAsync("PwListWatcher");
		await creator.LoginAsync();
		await watcher.LoginAsync();
		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("pw list room", Secret)));
		var matchId = BitConverter.ToUInt16((await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess))!.Payload, 0);

		await watcher.SendAsync(writer => writer.Packet(OsuClient.Out.JoinLobby));

		var listed = await watcher.WaitForAsync(OsuClient.In.NewMatch,
			packet => BitConverter.ToUInt16(packet.Payload, 0) == matchId);
		Assert.NotNull(listed);
		Assert.Equal(string.Empty, PasswordOf(listed));
	}
}
