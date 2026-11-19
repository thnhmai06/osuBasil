using Basil.Application.Beatmaps.Messages;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Registry;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Host.Shared.Views;

public sealed record CreateRoomRequest(string? Name, string? Password, bool? IsPrivate);

public sealed record UserRequest(int User);

public sealed record MoveRequest(int User, int Slot);

public sealed record TeamRequest(int User, GameTeam Team);

public sealed record SizeRequest(int Size);

public sealed record SetRequest(GameTeamType TeamType, GameWinCondition? WinCondition, int? Size);

public sealed record MapRequest(int Beatmap, GameMode? Mode);

public sealed record ModsRequest(string[]? Mods, bool? Freemod);

public sealed record CountdownRequest(int? Seconds);

public sealed record PasswordRequest(string? Password);

public sealed record NameRequest(string? Name);

public sealed record PrivateRequest(bool Private);

public readonly record struct RoomCall(ISender Sender, Session Actor, Room Room)
{
	public Task<RoomActionResult> Ask(IRequest<RoomActionResult> request, CancellationToken ct)
	{
		return Sender.Send(request, ct).AsTask();
	}
}

public sealed record RoomCommand(
	string Name,
	string Summary,
	Type? Body,
	Func<RoomCall, object?, IServiceProvider, CancellationToken, Task<RoomActionResult>> Run);

public static class RoomCommands
{
	public const int MaxRoomNameLength = 50;

	public static IReadOnlyList<RoomCommand> All { get; } =
	[
		Command<object?>("close", "Close the room.", (r, _, _, ct) => r.Ask(new CloseRoom(r.Actor, r.Room), ct)),
		Command<object?>("lock", "Lock the room so nobody else can join.",
			(r, _, _, ct) => r.Ask(new SetRoomLocked(r.Actor, r.Room, true), ct)),
		Command<object?>("unlock", "Unlock the room so players can join again.",
			(r, _, _, ct) => r.Ask(new SetRoomLocked(r.Actor, r.Room, false), ct)),
		Command<SizeRequest>("size", "Update the number of usable slots.",
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Size: b.Size)), ct)),
		Command<SetRequest>("set", "Update the team mode, the win condition and optionally the size.",
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(TeamType: b.TeamType,
				WinCondition: b.WinCondition, Size: b.Size)), ct)),
		Command<NameRequest>("name", "Update the name of the room.", (r, b, _, ct) =>
			r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Truncate(b.Name ?? string.Empty))), ct)),
		Command<PasswordRequest>("password", "Update the password of the room; empty removes it.",
			(r, b, _, ct) =>
				r.Ask(new ConfigureRoom(r.Actor, r.Room,
					new RoomSettingsChange(Password: b.Password ?? string.Empty)), ct)),
		Command<PrivateRequest>("private", "Update whether the room is private.",
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(IsPrivate: b.Private)),
				ct)),
		Command<MapRequest>("map", "Update the beatmap of the room.", MapAsync),
		Command<ModsRequest>("mods", "Update the mods of the room, or free mods.", (r, b, _, ct) =>
		{
			var mods = GameMods.NoMod;
			foreach (var token in b.Mods ?? [])
				if (!token.Equals("None", StringComparison.OrdinalIgnoreCase))
					mods |= ModsExtensions.FromModString(token);

			return r.Ask(
				new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Mods: mods, Freemods: b.Freemod ?? false)),
				ct);
		}),
		Command<MoveRequest>("move", "Move a player to a slot.",
			(r, b, _, ct) => r.Ask(new MovePlayer(r.Actor, r.Room, UserReference.ById(b.User), b.Slot), ct)),
		Command<TeamRequest>("team", "Update the team of a player.",
			(r, b, _, ct) => r.Ask(new SetPlayerTeam(r.Actor, r.Room, UserReference.ById(b.User), b.Team), ct)),
		Command<UserRequest>("host", "Update the host of the room to a player.", (r, b, s, ct) =>
		{
			var host = OnlineUser(s, b.User)?.Stable;
			return host is { IsOpen: true }
				? r.Ask(new SetRoomHost(r.Actor, r.Room, host), ct)
				: Refuse(RoomActionResult.TargetOffline);
		}),
		Command<object?>("clearhost", "Remove the host of the room.",
			(r, _, _, ct) => r.Ask(new SetRoomHost(r.Actor, r.Room, null), ct)),
		Command<UserRequest>("kick", "Remove a player from the room.",
			(r, b, _, ct) => r.Ask(new KickPlayer(r.Actor, r.Room, UserReference.ById(b.User)), ct)),
		Command<UserRequest>("ban", "Ban a player from the room.",
			(r, b, _, ct) => r.Ask(new BanPlayer(r.Actor, r.Room, UserReference.ById(b.User)), ct)),
		Command<UserRequest>("unban", "Remove the ban of a player.",
			(r, b, _, ct) => r.Ask(new UnbanPlayer(r.Actor, r.Room, UserReference.ById(b.User)), ct)),
		Command<UserRequest>("invite", "Invite an online player to the room.", (r, b, s, ct) =>
			OnlineUser(s, b.User) is { } target
				? r.Ask(new InvitePlayer(r.Actor, r.Room, target), ct)
				: Refuse(RoomActionResult.TargetOffline)),
		Command<UserRequest>("addref", "Add a player as a referee.",
			(r, b, _, ct) => r.Ask(new AddReferee(r.Actor, r.Room, UserReference.ById(b.User)), ct)),
		Command<UserRequest>("removeref", "Remove a referee of the room.",
			(r, b, _, ct) => r.Ask(new RemoveReferee(r.Actor, r.Room, UserReference.ById(b.User)), ct)),
		Command<CountdownRequest>("start", "Start the round, or start it after a countdown.", (r, b, _, ct) =>
			b.Seconds is { } seconds and > 0
				? Countdown(r, seconds, true, ct)
				: r.Ask(new StartRound(r.Actor, r.Room), ct)),
		Command<CountdownRequest>("timer", "Start a countdown of 30 seconds or the given length.",
			(r, b, _, ct) => Countdown(r, b.Seconds ?? 30, false, ct)),
		Command<object?>("aborttimer", "Abort the running countdown.",
			(r, _, _, ct) => r.Ask(new CancelRoomCountdown(r.Actor, r.Room), ct)),
		Command<object?>("abort", "Abort the round in progress.",
			(r, _, _, ct) => r.Ask(new AbortRound(r.Actor, r.Room), ct))
	];

	public static string Truncate(string name)
	{
		return name.Length > MaxRoomNameLength ? name[..MaxRoomNameLength] : name;
	}

	private static RoomCommand Command<TBody>(string name, string summary,
		Func<RoomCall, TBody, IServiceProvider, CancellationToken, Task<RoomActionResult>> run)
	{
		return new RoomCommand(name, summary, typeof(TBody) == typeof(object) ? null : typeof(TBody),
			(room, body, services, ct) => run(room, (TBody)body!, services, ct));
	}

	private static Task<RoomActionResult> Refuse(RoomActionResult outcome)
	{
		return Task.FromResult(outcome);
	}

	private static Task<RoomActionResult> Countdown(RoomCall room, int seconds, bool startsRound,
		CancellationToken ct)
	{
		return room.Ask(new StartRoomCountdown(room.Actor, room.Room, TimeSpan.FromSeconds(seconds), startsRound), ct);
	}

	private static async Task<RoomActionResult> MapAsync(RoomCall room, MapRequest body, IServiceProvider services,
		CancellationToken ct)
	{
		var beatmap = await services.GetRequiredService<ISender>().Send(new FindBeatmap(body.Beatmap), ct);
		if (beatmap is null) return RoomActionResult.NoBeatmap;

		var reference = new BeatmapReference(beatmap.Hash, beatmap.Id, beatmap.FullName,
			beatmap.Metadata.Difficulty.Mode,
			beatmap);
		return await room.Ask(new ConfigureRoom(room.Actor, room.Room,
			new RoomSettingsChange(Beatmap: reference, Mode: body.Mode ?? beatmap.Metadata.Difficulty.Mode)), ct);
	}

	private static User? OnlineUser(IServiceProvider services, int id)
	{
		return services.GetRequiredService<UserRegistry>().Online.FirstOrDefault(pair => pair.Key.Id == id).Value;
	}
}