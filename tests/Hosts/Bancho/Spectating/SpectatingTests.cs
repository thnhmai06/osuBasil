using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Spectating;

/// <summary>Spectating as the osu! client does it: the host's frames reach every spectator unchanged.</summary>
[Collection("Bancho host")]
public sealed class SpectatingTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task StartSpectating_TellsTheHost_AndLaterSpectatorsLearnOfEachOther()
	{
		var host = await Host.NewClientAsync("SpecHost");
		var first = await Host.NewClientAsync("SpecFirst");
		var second = await Host.NewClientAsync("SpecSecond");
		await host.LoginAsync();
		await first.LoginAsync();
		await second.LoginAsync();

		await first.SendAsync(writer => writer.Int(OsuClient.Out.StartSpectating, host.LoginCode));
		Assert.NotNull(await host.WaitForAsync(OsuClient.In.SpectatorJoined, packet => packet.AsInt() == first.LoginCode));
		await second.SendAsync(writer => writer.Int(OsuClient.Out.StartSpectating, host.LoginCode));

		Assert.NotNull(await first.WaitForAsync(OsuClient.In.FellowSpectatorJoined, packet => packet.AsInt() == second.LoginCode));
		Assert.NotNull(await second.WaitForAsync(OsuClient.In.FellowSpectatorJoined, packet => packet.AsInt() == first.LoginCode));
	}

	[Fact]
	public async Task Frames_FromTheHost_ReachTheSpectatorByteForByte()
	{
		var host = await Host.NewClientAsync("FrameHost");
		var spectator = await Host.NewClientAsync("FrameWatcher");
		await host.LoginAsync();
		await spectator.LoginAsync();
		await spectator.SendAsync(writer => writer.Int(OsuClient.Out.StartSpectating, host.LoginCode));
		await host.WaitForAsync(OsuClient.In.SpectatorJoined);
		var bundle = ClientPacketWriter.FrameBundle(0, 7, ClientPacketWriter.ScoreFrame(500, 999, 3));

		await host.SendAsync(writer => writer.Packet(OsuClient.Out.SpectateFrames, bundle));
		var relayed = await spectator.WaitForAsync(OsuClient.In.SpectateFrames);

		Assert.NotNull(relayed);
		Assert.Equal(bundle, relayed.Payload);
	}

	[Fact]
	public async Task Frames_MalformedBundle_StillReachTheSpectator()
	{
		var host = await Host.NewClientAsync("BadFrameHost");
		var spectator = await Host.NewClientAsync("BadFrameWatch");
		await host.LoginAsync();
		await spectator.LoginAsync();
		await spectator.SendAsync(writer => writer.Int(OsuClient.Out.StartSpectating, host.LoginCode));
		await host.WaitForAsync(OsuClient.In.SpectatorJoined);

		await host.SendAsync(writer => writer.Packet(OsuClient.Out.SpectateFrames, [1, 2, 3]));

		var relayed = await spectator.WaitForAsync(OsuClient.In.SpectateFrames);
		Assert.Equal(new byte[] { 1, 2, 3 }, relayed?.Payload);
	}

	[Fact]
	public async Task StopSpectating_TellsTheHost()
	{
		var host = await Host.NewClientAsync("StopHost");
		var spectator = await Host.NewClientAsync("StopWatcher");
		await host.LoginAsync();
		await spectator.LoginAsync();
		await spectator.SendAsync(writer => writer.Int(OsuClient.Out.StartSpectating, host.LoginCode));
		await host.WaitForAsync(OsuClient.In.SpectatorJoined);

		await spectator.SendAsync(writer => writer.Packet(OsuClient.Out.StopSpectating));

		Assert.NotNull(await host.WaitForAsync(OsuClient.In.SpectatorLeft, packet => packet.AsInt() == spectator.LoginCode));
	}

	[Fact]
	public async Task CantSpectate_TellsTheHostWhoLacksTheBeatmap()
	{
		var host = await Host.NewClientAsync("CantHost");
		var spectator = await Host.NewClientAsync("CantWatcher");
		await host.LoginAsync();
		await spectator.LoginAsync();
		await spectator.SendAsync(writer => writer.Int(OsuClient.Out.StartSpectating, host.LoginCode));
		await host.WaitForAsync(OsuClient.In.SpectatorJoined);

		await spectator.SendAsync(writer => writer.Packet(OsuClient.Out.CantSpectate));

		Assert.NotNull(await host.WaitForAsync(OsuClient.In.SpectatorCantSpectate, packet => packet.AsInt() == spectator.LoginCode));
	}
}
