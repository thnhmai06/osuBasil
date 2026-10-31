using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Capabilities.Rooms;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
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

/// <summary>One room command: a name, the body it takes, and what it does to a room scoped to the caller.</summary>
/// <param name="Name">The route segment, also the console word.</param>
/// <param name="Summary">One sentence that says what the command does.</param>
/// <param name="Body">The type of the body, or <see langword="null" /> when the command takes none.</param>
/// <param name="Run">Runs the command on a scoped room with the body.</param>
public sealed record RoomCommand(
	string Name,
	string Summary,
	Type? Body,
	Func<ScopedRoom, object?, IServiceProvider, CancellationToken, Task<Result<RoomOutcome>>> Run);

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
		Command<object?>("close", "Close the room.", (r, _, _, ct) => r.CloseAsync(ct)),
		Command<object?>("lock", "Lock the room so nobody else can join.",
			(r, _, _, ct) => r.Settings.SetLockedAsync(true, ct)),
		Command<object?>("unlock", "Unlock the room.", (r, _, _, ct) => r.Settings.SetLockedAsync(false, ct)),
		Command<SizeRequest>("size", "Change the number of usable slots.", (r, b, _, ct) =>
			b.Size is < 1 or > MaxRoomSize
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Settings.ConfigureAsync(new RoomSettingsChange(Size: b.Size), ct)),
		Command<SetRequest>("set", "Change the team mode, the win condition and optionally the size.",
			(r, b, _, ct) => b.Size is < 1 or > MaxRoomSize
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Settings.ConfigureAsync(new RoomSettingsChange(TeamType: b.TeamType, WinCondition: b.WinCondition,
					Size: b.Size), ct)),
		Command<NameRequest>("name", "Rename the room.", (r, b, _, ct) =>
			string.IsNullOrWhiteSpace(b.Name)
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Settings.ConfigureAsync(new RoomSettingsChange(Truncate(b.Name)), ct)),
		Command<PasswordRequest>("password", "Change the password of the room; empty removes it.",
			(r, b, _, ct) =>
				r.Settings.ConfigureAsync(new RoomSettingsChange(Password: b.Password ?? string.Empty), ct)),
		Command<PrivateRequest>("private", "Make the room private or public.",
			(r, b, _, ct) => r.Settings.ConfigureAsync(new RoomSettingsChange(IsPrivate: b.Private), ct)),
		Command<MapRequest>("map", "Select the beatmap of the room.", MapAsync),
		Command<ModsRequest>("mods", "Set the mods of the room, or free mods.", (r, b, _, ct) =>
		{
			var mods = GameMods.NoMod;
			foreach (var token in b.Mods ?? [])
				if (!token.Equals("None", StringComparison.OrdinalIgnoreCase))
					mods |= ModsExtensions.FromModString(token);

			return r.Settings.ConfigureAsync(new RoomSettingsChange(Mods: mods, Freemods: b.Freemod ?? false), ct);
		}),
		Command<MoveRequest>("move", "Move a player to a slot.", (r, b, s, ct) => WithUser(s, b.User, ct,
			u => b.Slot is < 1 or > MaxRoomSize
				? Refuse(RoomOutcome.InvalidSettings)
				: r.Slots.MoveAsync(u, b.Slot, ct))),
		Command<TeamRequest>("team", "Put a player in a team.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Slots.SetTeamAsync(u, b.Team, ct))),
		Command<UserRequest>("host", "Give the host to a player.", (r, b, s, ct) => WithUser(s, b.User, ct, u =>
		{
			var host = s.GetRequiredService<UserRegistry>().Online.GetValueOrDefault(u)?.Bancho;
			return host is { IsOpen: true } ? r.Authority.SetHostAsync(host, ct) : Refuse(RoomOutcome.TargetOffline);
		})),
		Command<object?>("clearhost", "Remove the host.", (r, _, _, ct) => r.Authority.SetHostAsync(null, ct)),
		Command<UserRequest>("kick", "Remove a player from the room.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Members.KickAsync(u, ct))),
		Command<UserRequest>("ban", "Ban a player from the room.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Members.BanAsync(u, ct))),
		Command<UserRequest>("unban", "Lift the ban of a player.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Members.UnbanAsync(u, ct))),
		Command<UserRequest>("invite", "Invite an online player to the room.", (r, b, s, ct) => WithUser(s, b.User, ct,
			u =>
			{
				var target = s.GetRequiredService<UserRegistry>().Online.GetValueOrDefault(u);
				return target is null ? Refuse(RoomOutcome.TargetOffline) : r.Members.InviteAsync(target, ct);
			})),
		Command<UserRequest>("addref", "Make a player a referee.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Authority.AddRefereeAsync(u, ct))),
		Command<UserRequest>("removeref", "Remove a referee.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Authority.RemoveRefereeAsync(u, ct))),
		Command<CountdownRequest>("start", "Start the round, or start it after a countdown.", (r, b, _, ct) =>
			b.Seconds is { } seconds and > 0 ? Countdown(r, seconds, true, ct) : r.Rounds.StartAsync(ct)),
		Command<CountdownRequest>("timer", "Start a countdown of 30 seconds or the given length.",
			(r, b, _, ct) => Countdown(r, b.Seconds ?? 30, false, ct)),
		Command<object?>("aborttimer", "Cancel the running countdown.",
			(r, _, _, ct) => r.Rounds.CancelCountdownAsync(ct)),
		Command<object?>("abort", "Abort the round in progress.", (r, _, _, ct) => r.Rounds.AbortAsync(ct))
	];

	/// <summary>Cuts a room name to the longest length.</summary>
	public static string Truncate(string name)
	{
		return name.Length > MaxRoomNameLength ? name[..MaxRoomNameLength] : name;
	}

	private static RoomCommand Command<TBody>(string name, string summary,
		Func<ScopedRoom, TBody, IServiceProvider, CancellationToken, Task<Result<RoomOutcome>>> run)
	{
		return new RoomCommand(name, summary, typeof(TBody) == typeof(object) ? null : typeof(TBody),
			(room, body, services, ct) => run(room, (TBody)body!, services, ct));
	}

	private static Task<Result<RoomOutcome>> Refuse(RoomOutcome outcome)
	{
		return Task.FromResult<Result<RoomOutcome>>(outcome);
	}

	private static Task<Result<RoomOutcome>> Countdown(ScopedRoom room, int seconds, bool startsRound,
		CancellationToken ct)
	{
		return seconds < 1 || TimeSpan.FromSeconds(seconds) > ScopedRoomRounds.MaxCountdown
			? Refuse(RoomOutcome.InvalidSettings)
			: room.Rounds.StartCountdownAsync(TimeSpan.FromSeconds(seconds), startsRound, ct);
	}

	private static async Task<Result<RoomOutcome>> MapAsync(ScopedRoom room, MapRequest body, IServiceProvider services,
		CancellationToken ct)
	{
		var beatmap = await services.GetRequiredService<IBeatmapRepository>().GetAsync(body.Beatmap, ct);
		if (beatmap is null) return RoomOutcome.NoBeatmap;

		var reference = new BeatmapReference(beatmap.Hash, beatmap.Id, beatmap.Name, beatmap.Metadata.Difficulty.Mode,
			beatmap);
		return await room.Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: reference, Mode: body.Mode ?? beatmap.Metadata.Difficulty.Mode), ct);
	}

	private static async Task<Result<RoomOutcome>> WithUser(IServiceProvider services, int id, CancellationToken ct,
		Func<UserRecord, Task<Result<RoomOutcome>>> act)
	{
		var user = await services.GetRequiredService<IUserRepository>().GetAsync(id, ct);
		// An unknown user cannot be in the room, so the operation is refused as a state conflict.
		return user is null ? RoomOutcome.NotInRoom : await act(user);
	}
}