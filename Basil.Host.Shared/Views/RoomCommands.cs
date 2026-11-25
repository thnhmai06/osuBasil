using Basil.Application.Beatmaps.Messages;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Host.Shared.Views;

public sealed record CreateRoomRequest(string? Name, string? Password, bool? IsPrivate);

public sealed record UserRequest(int User);

public sealed record MoveRequest(int User, int Slot);

public sealed record TeamRequest(int User, GameTeam Team);

public sealed record SizeRequest(int Size);

public sealed record SetRequest(GameTeamType TeamType, GameWinCondition? WinCondition, int? Size);

public sealed record MapRequest(int Beatmap, GameMode? Mode);

public sealed record ModsRequest(GameMods? Mods, bool? Freemod);

public sealed record CountdownRequest(int? Seconds);

public sealed record PasswordRequest(string? Password);

public sealed record NameRequest(string? Name);

public sealed record PrivateRequest(bool Private);

public readonly record struct RoomCall(ISender Sender, Session Actor, Room Room)
{
	public async Task<Enum> Ask<TOutcome>(IRequest<TOutcome> request, CancellationToken ct)
		where TOutcome : struct, Enum
	{
		return await Sender.Send(request, ct);
	}
}

public sealed record RoomCommand(
	string Name,
	string Summary,
	RoomPrivileges Roles,
	Type? Body,
	Func<RoomCall, object?, IServiceProvider, CancellationToken, Task<Enum>> Run);

public static class RoomCommands
{
	public const int MaxRoomNameLength = 50;

	public static IReadOnlyList<RoomCommand> All { get; } =
	[
		Command<object?>("close", "Close the room.", RoomPrivileges.Moderators, (r, _, _, ct) => r.Ask(new CloseRoom(r.Actor, r.Room), ct)),
		Command<object?>("lock", "Lock the room so nobody else can join.", RoomPrivileges.Moderators,
			(r, _, _, ct) => r.Ask(new SetRoomLocked(r.Actor, r.Room, true), ct)),
		Command<object?>("unlock", "Unlock the room so players can join again.", RoomPrivileges.Moderators,
			(r, _, _, ct) => r.Ask(new SetRoomLocked(r.Actor, r.Room, false), ct)),
		Command<SizeRequest>("size", "Update the number of usable slots.", RoomPrivileges.Staff,
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Size: b.Size)), ct)),
		Command<SetRequest>("set", "Update the team mode, the win condition and optionally the size.", RoomPrivileges.Staff,
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(TeamType: b.TeamType,
				WinCondition: b.WinCondition, Size: b.Size)), ct)),
		Command<NameRequest>("name", "Update the name of the room.", RoomPrivileges.Staff, (r, b, _, ct) =>
			r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Truncate(b.Name ?? string.Empty))), ct)),
		Command<PasswordRequest>("password", "Update the password of the room; empty removes it.", RoomPrivileges.Staff,
			(r, b, _, ct) =>
				r.Ask(new ConfigureRoom(r.Actor, r.Room,
					new RoomSettingsChange(Password: b.Password ?? string.Empty)), ct)),
		Command<PrivateRequest>("private", "Update whether the room is private.", RoomPrivileges.Moderators,
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(IsPrivate: b.Private)),
				ct)),
		Command<MapRequest>("map", "Update the beatmap of the room.", RoomPrivileges.Staff,
			(r, b, _, ct) => r.Ask(new SetRoomBeatmap(r.Actor, r.Room, b.Beatmap, b.Mode), ct)),
		Command<ModsRequest>("mods", "Update the mods of the room, or free mods.", RoomPrivileges.Staff, (r, b, _, ct) =>
		{
			return r.Ask(
				new ConfigureRoom(r.Actor, r.Room,
					new RoomSettingsChange(Mods: b.Mods ?? GameMods.NoMod, Freemods: b.Freemod ?? false)), ct);
		}),
		Command<MoveRequest>("move", "Move a player to a slot.", RoomPrivileges.Moderators,
			(r, b, _, ct) => r.Ask(new MovePlayer(r.Actor, r.Room, b.User, b.Slot), ct)),
		Command<TeamRequest>("team", "Update the team of a player.", RoomPrivileges.Moderators,
			(r, b, _, ct) => r.Ask(new SetPlayerTeam(r.Actor, r.Room, b.User, b.Team), ct)),
		Command<UserRequest>("host", "Update the host of the room to a player.", RoomPrivileges.Staff, (r, b, _, ct) => r.Ask(new SetRoomHost(r.Actor, r.Room, b.User), ct)),
		Command<object?>("clearhost", "Remove the host of the room.", RoomPrivileges.Staff,
			(r, _, _, ct) => r.Ask(new SetRoomHost(r.Actor, r.Room, null), ct)),
		Command<UserRequest>("kick", "Remove a player from the room.", RoomPrivileges.Staff,
			(r, b, _, ct) => r.Ask(new KickPlayer(r.Actor, r.Room, b.User), ct)),
		Command<UserRequest>("ban", "Ban a player from the room.", RoomPrivileges.Moderators,
			(r, b, _, ct) => r.Ask(new BanPlayer(r.Actor, r.Room, b.User), ct)),
		Command<UserRequest>("unban", "Remove the ban of a player.", RoomPrivileges.Moderators,
			(r, b, _, ct) => r.Ask(new UnbanPlayer(r.Actor, r.Room, b.User), ct)),
		Command<UserRequest>("invite", "Invite an online player to the room.", RoomPrivileges.Player | RoomPrivileges.Moderators, (r, b, _, ct) => r.Ask(new InvitePlayer(r.Actor, r.Room, b.User), ct)),
		Command<UserRequest>("addref", "Add a player as a referee.", RoomPrivileges.Creator,
			(r, b, _, ct) => r.Ask(new AddReferee(r.Actor, r.Room, b.User), ct)),
		Command<UserRequest>("removeref", "Remove a referee of the room.", RoomPrivileges.Creator,
			(r, b, _, ct) => r.Ask(new RemoveReferee(r.Actor, r.Room, b.User), ct)),
		Command<CountdownRequest>("start", "Start the round, or start it after a countdown.", RoomPrivileges.Staff, (r, b, _, ct) =>
			b.Seconds is { } seconds and > 0
				? Countdown(r, seconds, true, ct)
				: r.Ask(new StartRound(r.Actor, r.Room), ct)),
		Command<CountdownRequest>("timer", "Start a countdown of 30 seconds or the given length.", RoomPrivileges.Staff,
			(r, b, _, ct) => Countdown(r, b.Seconds ?? 30, false, ct)),
		Command<object?>("aborttimer", "Abort the running countdown.", RoomPrivileges.Staff,
			(r, _, _, ct) => r.Ask(new CancelRoomCountdown(r.Actor, r.Room), ct)),
		Command<object?>("abort", "Abort the round in progress.", RoomPrivileges.Staff,
			(r, _, _, ct) => r.Ask(new AbortRound(r.Actor, r.Room), ct))
	];

	/// <summary>The description of a command in the API reference: who may give it, what it reads and what it answers.</summary>
	public static string Describe(RoomCommand command)
	{
		var body = command.Body is null
			? "It takes no body."
			: "A field that is missing or of the wrong type is answered with 422 and the field's name.";
		return $"{command.Summary} A caller who may not give it is refused with 403. {body} The answer is the room as it is after the command. A room that does not exist or that the caller may not view is answered with 404, and a closed room with 410.\n\nThe room's other rules can refuse the command with 409 or 422.";
	}

	public static string Truncate(string name)
	{
		return name.Length > MaxRoomNameLength ? name[..MaxRoomNameLength] : name;
	}

	private static RoomCommand Command<TBody>(string name, string summary, RoomPrivileges roles,
		Func<RoomCall, TBody, IServiceProvider, CancellationToken, Task<Enum>> run)
	{
		return new RoomCommand(name, summary, roles, typeof(TBody) == typeof(object) ? null : typeof(TBody),
			(room, body, services, ct) => run(room, (TBody)body!, services, ct));
	}

	private static Task<Enum> Countdown(RoomCall room, int seconds, bool startsRound,
		CancellationToken ct)
	{
		return room.Ask(new StartRoomCountdown(room.Actor, room.Room, TimeSpan.FromSeconds(seconds), startsRound), ct);
	}

}