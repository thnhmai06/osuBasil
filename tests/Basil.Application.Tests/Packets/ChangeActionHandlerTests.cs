using System.Text.Json;
using Basil.Application.Formats;
using Basil.Application.Packets.Users;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `ChangeAction` handler updates the player's action/status and broadcasts the change.</summary>
public class ChangeActionHandlerTests
{
	private readonly ISessionRegistry<GameSession> _sessionRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly MultiplayerTestSupport.FakePlayerStatusEvents _statusEvents = new();

	private static byte[] Payload(int action, string infoText, string mapMd5, uint mods, byte mode, int mapId)
	{
		return
		[
			(byte)action,
			.. BinaryWriter.WriteString(infoText),
			.. BinaryWriter.WriteString(mapMd5),
			.. BinaryWriter.WriteUInt32(mods),
			mode,
			.. BinaryWriter.WriteInt32(mapId)
		];
	}

	[Fact]
	public async Task Handle_UpdatesStatusFields()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var reader =
			new PacketReader(Payload((int)UserActivity.Playing, "playing a map", "abc123", (uint)Mods.Hidden, 0,
				42));

		await new ChangeActionHandler(_sessionRegistry, _statusEvents).HandleAsync(session, reader);

		Assert.Equal(UserActivity.Playing, session.Status.UserActivity);
		Assert.Equal("playing a map", session.Status.InfoText);
		Assert.Equal("abc123", session.Status.MapMd5);
		Assert.Equal(Mods.Hidden, session.Status.Mods);
		Assert.Equal(GameMode.Standard, session.Status.Mode);
		Assert.Equal(42, session.Status.MapId);
	}

	/// <summary>
	///     Regression test (Issue #4 follow-up: "GET /users/{id}/live should include status
	///     information"): an activity change must publish the updated status to the userSession's live
	///     status channel.
	/// </summary>
	[Fact]
	public async Task Handle_PublishesUpdatedStatus()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var reader = new PacketReader(Payload((int)UserActivity.Playing, "playing a map", "abc123",
			(uint)Mods.Hidden, 0, 42));

		await new ChangeActionHandler(_sessionRegistry, _statusEvents).HandleAsync(session, reader);

		var publish = Assert.Single(_statusEvents.Publishes);
		Assert.Equal(1, publish.PlayerId);
		var view = JsonSerializer.Deserialize<PlayerStatusView>(publish.Payload, BasilJsonOptions.Instance);
		Assert.True(view!.Online);
		Assert.Equal(UserActivity.Playing, view.Activity);
		Assert.Equal(42, view.MapId);
	}

	[Fact]
	public async Task Handle_RelaxMod_PassesThroughModeAndModsUnchanged()
	{
		// GameMode is a plain 4-value enum — Relax/Autopilot are ordinary Mods bits now, with no
		// effect on Mode, unlike the old Vanilla/Relax/Autopilot-variant enum.
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var reader = new PacketReader(Payload(0, "", "", (uint)Mods.Relax, 0, 0));

		await new ChangeActionHandler(_sessionRegistry, _statusEvents).HandleAsync(session, reader);

		Assert.Equal(GameMode.Standard, session.Status.Mode);
		Assert.Equal(Mods.Relax, session.Status.Mods);
	}

	[Fact]
	public async Task Handle_AutopilotModOnMania_PassesThroughModeAndModsUnchanged()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var reader = new PacketReader(Payload(0, "", "", (uint)Mods.Autopilot, 3, 0));

		await new ChangeActionHandler(_sessionRegistry, _statusEvents).HandleAsync(session, reader);

		Assert.Equal(GameMode.Mania, session.Status.Mode);
		Assert.Equal(Mods.Autopilot, session.Status.Mods);
	}

	[Fact]
	public async Task Handle_Unrestricted_BroadcastsUpdatedStatsToAllOnlinePlayers()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.All.Returns([session, other]);
		var reader = new PacketReader(Payload((int)UserActivity.Idle, "", "", 0, 0, 0));

		await new ChangeActionHandler(_sessionRegistry, _statusEvents).HandleAsync(session, reader);

		Assert.NotEmpty(other.Dequeue());
	}

	[Fact]
	public async Task Handle_Restricted_DoesNotBroadcast()
	{
		var session =
			new GameSession(1, "cmyui", "token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch); // restricted
		var other = new GameSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.All.Returns([session, other]);
		var reader = new PacketReader(Payload(0, "", "", 0, 0, 0));

		await new ChangeActionHandler(_sessionRegistry, _statusEvents).HandleAsync(session, reader);

		Assert.Empty(other.Dequeue());
	}
}