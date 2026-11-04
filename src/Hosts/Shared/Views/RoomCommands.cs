using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Shared.Views;

/// <summary>The body of a room creation.</summary>
public sealed record CreateRoomRequest(string? Name, string? Password, bool? IsPrivate);

/// <summary>The body of a command that names a user.</summary>
public sealed record UserRequest(int User);

/// <summary>The body of <c>/move</c>.</summary>
public sealed record MoveRequest(int User, int Slot);

/// <summary>The body of <c>/team</c>.</summary>
public sealed record TeamRequest(int User, GameTeam Team);

/// <summary>The body of <c>/size</c>.</summary>
public sealed record SizeRequest(int Size);

/// <summary>The body of <c>/set</c>.</summary>
public sealed record SetRequest(GameTeamType TeamType, GameWinCondition? WinCondition, int? Size);

/// <summary>The body of <c>/map</c>.</summary>
public sealed record MapRequest(int Beatmap, GameMode? Mode);

/// <summary>The body of <c>/mods</c>; mods are acronyms such as <c>HD</c>.</summary>
public sealed record ModsRequest(string[]? Mods, bool? Freemod);

/// <summary>The body of <c>/start</c> and <c>/timer</c>.</summary>
public sealed record CountdownRequest(int? Seconds);

/// <summary>The body of <c>/password</c>; an empty password removes it.</summary>
public sealed record PasswordRequest(string? Password);

/// <summary>The body of <c>/name</c>.</summary>
public sealed record NameRequest(string? Name);

/// <summary>The body of <c>/private</c>.</summary>
public sealed record PrivateRequest(bool Private);

/// <summary>The room a command acts on and the connection that asks, with the way to send the request.</summary>
/// <param name="Sender">Sends the request.</param>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Room">The room the command acts on.</param>
public readonly record struct RoomCall(ISender Sender, Connection Actor, Room Room)
{
	/// <summary>Sends a room request and returns its answer.</summary>
	public Task<Result<RoomOutcome>> Ask(IRequest<Result<RoomOutcome>> request, CancellationToken cancellationToken)
	{
		return Sender.Send(request, cancellationToken).AsTask();
	}
}

/// <summary>One room command: a name, the body it takes, and what it asks of a room for the caller.</summary>
/// <param name="Name">The route segment, also the console word.</param>
/// <param name="Summary">One sentence that says what the command does.</param>
/// <param name="Body">The type of the body, or <see langword="null" /> when the command takes none.</param>
/// <param name="Run">Runs the command on a room with the body.</param>
public sealed record RoomCommand(
	string Name,
	string Summary,
	Type? Body,
	Func<RoomCall, object?, IServiceProvider, CancellationToken, Task<Result<RoomOutcome>>> Run);

/// <summary>The room commands that both the HTTP API and the console offer, one for each room command of <c>!mp</c>.</summary>
public static class RoomCommands
{
	/// <summary>The longest room name.</summary>
	public const int MaxRoomNameLength = 50;

	/// <summary>The most slots a room has.</summary>
	public const int MaxRoomSize = 16;

	/// <summary>Gets every room command.</summary>
	public static IReadOnlyList<RoomCommand> All { get; } =
	[
		Command<object?>("close", "Close the room.", (r, _, _, ct) => r.Ask(new CloseRoom(r.Actor, r.Room), ct)),
		Command<object?>("lock", "Lock the room so nobody else can join.",
			(r, _, _, ct) => r.Ask(new SetRoomLocked(r.Actor, r.Room, true), ct)),
		Command<object?>("unlock", "Unlock the room.", (r, _, _, ct) => r.Ask(new SetRoomLocked(r.Actor, r.Room, false), ct)),
		Command<SizeRequest>("size", "Change the number of usable slots.", (r, b, _, ct) =>
			b.Size is < 1 or > MaxRoomSize
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Size: b.Size)), ct)),
		Command<SetRequest>("set", "Change the team mode, the win condition and optionally the size.",
			(r, b, _, ct) => b.Size is < 1 or > MaxRoomSize
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(TeamType: b.TeamType,
					WinCondition: b.WinCondition, Size: b.Size)), ct)),
		Command<NameRequest>("name", "Rename the room.", (r, b, _, ct) =>
			string.IsNullOrWhiteSpace(b.Name)
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Truncate(b.Name))), ct)),
		Command<PasswordRequest>("password", "Change the password of the room; empty removes it.",
			(r, b, _, ct) =>
				r.Ask(new ConfigureRoom(r.Actor, r.Room,
					new RoomSettingsChange(Password: b.Password ?? string.Empty)), ct)),
		Command<PrivateRequest>("private", "Make the room private or public.",
			(r, b, _, ct) => r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(IsPrivate: b.Private)), ct)),
		Command<MapRequest>("map", "Select the beatmap of the room.", MapAsync),
		Command<ModsRequest>("mods", "Set the mods of the room, or free mods.", (r, b, _, ct) =>
		{
			var mods = GameMods.NoMod;
			foreach (var token in b.Mods ?? [])
				if (!token.Equals("None", StringComparison.OrdinalIgnoreCase))
					mods |= ModsExtensions.FromModString(token);

			return r.Ask(new ConfigureRoom(r.Actor, r.Room, new RoomSettingsChange(Mods: mods, Freemods: b.Freemod ?? false)),
				ct);
		}),
		Command<MoveRequest>("move", "Move a player to a slot.", (r, b, s, ct) => WithUser(s, b.User, ct,
			u => b.Slot is < 1 or > MaxRoomSize
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Ask(new MovePlayer(r.Actor, r.Room, u, b.Slot), ct))),
		Command<TeamRequest>("team", "Put a player in a team.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Ask(new SetPlayerTeam(r.Actor, r.Room, u, b.Team), ct))),
		Command<UserRequest>("host", "Give the host to a player.", (r, b, s, ct) => WithUser(s, b.User, ct, u =>
		{
			var host = s.GetRequiredService<UserRegistry>().Online.GetValueOrDefault(u)?.Stable;
			return host is { IsOpen: true } ? r.Ask(new SetRoomHost(r.Actor, r.Room, host), ct) : Refuse(RoomOutcome.TargetOffline);
		})),
		Command<object?>("clearhost", "Remove the host.", (r, _, _, ct) => r.Ask(new SetRoomHost(r.Actor, r.Room, null), ct)),
		Command<UserRequest>("kick", "Remove a player from the room.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Ask(new KickPlayer(r.Actor, r.Room, u), ct))),
		Command<UserRequest>("ban", "Ban a player from the room.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Ask(new BanPlayer(r.Actor, r.Room, u), ct))),
		Command<UserRequest>("unban", "Lift the ban of a player.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Ask(new UnbanPlayer(r.Actor, r.Room, u), ct))),
		Command<UserRequest>("invite", "Invite an online player to the room.", (r, b, s, ct) => WithUser(s, b.User, ct,
			u =>
			{
				var target = s.GetRequiredService<UserRegistry>().Online.GetValueOrDefault(u);
				return target is null ? Refuse(RoomOutcome.TargetOffline) : r.Ask(new InvitePlayer(r.Actor, r.Room, target), ct);
			})),
		Command<UserRequest>("addref", "Make a player a referee.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Ask(new AddReferee(r.Actor, r.Room, u), ct))),
		Command<UserRequest>("removeref", "Remove a referee.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Ask(new RemoveReferee(r.Actor, r.Room, u), ct))),
		Command<CountdownRequest>("start", "Start the round, or start it after a countdown.", (r, b, _, ct) =>
			b.Seconds is { } seconds and > 0 ? Countdown(r, seconds, true, ct) : r.Ask(new StartRound(r.Actor, r.Room), ct)),
		Command<CountdownRequest>("timer", "Start a countdown of 30 seconds or the given length.",
			(r, b, _, ct) => Countdown(r, b.Seconds ?? 30, false, ct)),
		Command<object?>("aborttimer", "Cancel the running countdown.",
			(r, _, _, ct) => r.Ask(new CancelRoomCountdown(r.Actor, r.Room), ct)),
		Command<object?>("abort", "Abort the round in progress.", (r, _, _, ct) => r.Ask(new AbortRound(r.Actor, r.Room), ct))
	];

	/// <summary>Cuts a room name to the longest length.</summary>
	public static string Truncate(string name)
	{
		return name.Length > MaxRoomNameLength ? name[..MaxRoomNameLength] : name;
	}

	private static RoomCommand Command<TBody>(string name, string summary,
		Func<RoomCall, TBody, IServiceProvider, CancellationToken, Task<Result<RoomOutcome>>> run)
	{
		return new RoomCommand(name, summary, typeof(TBody) == typeof(object) ? null : typeof(TBody),
			(room, body, services, ct) => run(room, (TBody)body!, services, ct));
	}

	private static Task<Result<RoomOutcome>> Refuse(RoomOutcome outcome)
	{
		return Task.FromResult<Result<RoomOutcome>>(outcome);
	}

	private static Task<Result<RoomOutcome>> Countdown(RoomCall room, int seconds, bool startsRound,
		CancellationToken ct)
	{
		return room.Ask(new StartRoomCountdown(room.Actor, room.Room, TimeSpan.FromSeconds(seconds), startsRound), ct);
	}

	private static async Task<Result<RoomOutcome>> MapAsync(RoomCall room, MapRequest body, IServiceProvider services,
		CancellationToken ct)
	{
		var beatmap = await services.GetRequiredService<IBeatmapRepository>().GetAsync(body.Beatmap, ct);
		if (beatmap is null) return RoomOutcome.NoBeatmap;

		var reference = new BeatmapReference(beatmap.Hash, beatmap.Id, beatmap.Name, beatmap.Metadata.Difficulty.Mode,
			beatmap);
		return await room.Ask(new ConfigureRoom(room.Actor, room.Room,
			new RoomSettingsChange(Beatmap: reference, Mode: body.Mode ?? beatmap.Metadata.Difficulty.Mode)), ct);
	}

	private static async Task<Result<RoomOutcome>> WithUser(IServiceProvider services, int id, CancellationToken ct,
		Func<UserRecord, Task<Result<RoomOutcome>>> act)
	{
		var user = await services.GetRequiredService<IUserRepository>().GetAsync(id, ct);
		// An unknown user cannot be in the room, so the operation is refused as a state conflict.
		return user is null ? RoomOutcome.NotInRoom : await act(user);
	}
}