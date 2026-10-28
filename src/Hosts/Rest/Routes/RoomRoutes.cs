using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Sse;

namespace Basil.Host.Rest.Routes;

/// <summary>Multiplayer rooms: reading them and one route for each room command of <c>!mp</c>.</summary>
internal static class RoomRoutes
{
	private const int MaxRoomNameLength = 50;
	private const int MaxRoomSize = 16;

	/// <summary>A user as another resource refers to them.</summary>
	public sealed record UserRef(int Id, string Name);

	/// <summary>A slot of a room.</summary>
	public sealed record SlotView(int Index, bool Locked, RoomSlotStatus? Status, GameTeam? Team, string? Mods,
		UserRef? Player, bool IsHost);

	/// <summary>A room as the API shows it; the password is never shown.</summary>
	public sealed record RoomView(int Id, int MatchId, string Name, bool IsTournament, bool IsPrivate, bool HasPassword, bool Locked,
		bool InProgress, UserRef Creator, UserRef? Host, IReadOnlyList<UserRef> Referees, IReadOnlyList<UserRef> Bans,
		int? BeatmapId, GameMode Mode, string Mods, bool Freemods, GameTeamType TeamType, GameWinCondition WinCondition,
		IReadOnlyList<SlotView> Slots);

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

	public static void MapRooms(this RouteGroupBuilder v1)
	{
		var rooms = v1.MapGroup("/rooms").WithTags("Rooms").RequireToken();

		rooms.MapGet("/", ListAsync).WithSummary("List the open rooms the caller may see.")
			.WithDescription("Private rooms are listed only for those who may view them.");
		rooms.MapPost("/", CreateAsync).WithSummary("Open a tournament room.")
			.WithDescription("The caller becomes its creator. While room creation is locked, only room managers can open one.");
		rooms.MapGet("/{id:int}", GetAsync).WithSummary("Retrieve a room with its slots, referees and bans.");
		rooms.MapGet("/{id:int}/live", LiveAsync).Sse().WithSummary("Watch a room change.")
			.WithDescription("""
				The response is streamed using Server-Sent Events. The first event is the room as `GET /v1/rooms/{id}` returns it; after that a bundle arrives every second in which the room changed, carrying the room as it is then. The stream ends when the room closes. It carries no gameplay or scores. Data is up to about one second old.
				""");

		Command<object?>(rooms, "close", "Close the room.", (r, _, _, ct) => r.CloseAsync(ct));
		Command<object?>(rooms, "lock", "Lock the room so nobody else can join.",
			(r, _, _, ct) => r.Settings.SetLockedAsync(true, ct));
		Command<object?>(rooms, "unlock", "Unlock the room.", (r, _, _, ct) => r.Settings.SetLockedAsync(false, ct));
		Command<SizeRequest>(rooms, "size", "Change the number of usable slots.", (r, b, _, ct) =>
			b.Size is < 1 or > MaxRoomSize
				? Task.FromResult<Result<RoomOutcome>>(RoomOutcome.InvalidSettings)
				: r.Settings.ConfigureAsync(new RoomSettingsChange(Size: b.Size), ct));
		Command<SetRequest>(rooms, "set", "Change the team mode, the win condition and optionally the size.",
			(r, b, _, ct) => b.Size is < 1 or > MaxRoomSize
				? Task.FromResult<Result<RoomOutcome>>(RoomOutcome.InvalidSettings)
				: r.Settings.ConfigureAsync(new RoomSettingsChange(TeamType: b.TeamType, WinCondition: b.WinCondition,
					Size: b.Size), ct));
		Command<NameRequest>(rooms, "name", "Rename the room.", (r, b, _, ct) =>
			string.IsNullOrWhiteSpace(b.Name)
				? Task.FromResult<Result<RoomOutcome>>(RoomOutcome.InvalidSettings)
				: r.Settings.ConfigureAsync(new RoomSettingsChange(Truncate(b.Name)), ct));
		Command<PasswordRequest>(rooms, "password", "Change the password of the room; empty removes it.",
			(r, b, _, ct) => r.Settings.ConfigureAsync(new RoomSettingsChange(Password: b.Password ?? string.Empty), ct));
		Command<PrivateRequest>(rooms, "private", "Make the room private or public.",
			(r, b, _, ct) => r.Settings.ConfigureAsync(new RoomSettingsChange(IsPrivate: b.Private), ct));
		Command<MapRequest>(rooms, "map", "Select the beatmap of the room.", MapAsync);
		Command<ModsRequest>(rooms, "mods", "Set the mods of the room, or free mods.", (r, b, _, ct) =>
		{
			var mods = GameMods.NoMod;
			foreach (var token in b.Mods ?? [])
				if (!token.Equals("None", StringComparison.OrdinalIgnoreCase))
					mods |= ModsExtensions.FromModString(token);

			return r.Settings.ConfigureAsync(new RoomSettingsChange(Mods: mods, Freemods: b.Freemod ?? false), ct);
		});

		Command<MoveRequest>(rooms, "move", "Move a player to a slot.", (r, b, s, ct) => WithUser(s, b.User, ct,
			u => b.Slot is < 1 or > MaxRoomSize
				? Task.FromResult<Result<RoomOutcome>>(RoomOutcome.InvalidSettings)
				: r.Slots.MoveAsync(u, b.Slot, ct)));
		Command<TeamRequest>(rooms, "team", "Put a player in a team.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Slots.SetTeamAsync(u, b.Team, ct)));
		Command<UserRequest>(rooms, "host", "Give the host to a player.", (r, b, s, ct) => WithUser(s, b.User, ct, u =>
		{
			var host = s.GetRequiredService<UserRegistry>().Online.GetValueOrDefault(u)?.Bancho;
			return host is { IsOpen: true }
				? r.Authority.SetHostAsync(host, ct)
				: Task.FromResult<Result<RoomOutcome>>(RoomOutcome.TargetOffline);
		}));
		Command<object?>(rooms, "clearhost", "Remove the host.", (r, _, _, ct) => r.Authority.SetHostAsync(null, ct));
		Command<UserRequest>(rooms, "kick", "Remove a player from the room.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Members.KickAsync(u, ct)));
		Command<UserRequest>(rooms, "ban", "Ban a player from the room.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Members.BanAsync(u, ct)));
		Command<UserRequest>(rooms, "unban", "Lift the ban of a player.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Members.UnbanAsync(u, ct)));
		Command<UserRequest>(rooms, "invite", "Invite an online player to the room.", (r, b, s, ct) =>
			WithUser(s, b.User, ct, u =>
			{
				var target = s.GetRequiredService<UserRegistry>().Online.GetValueOrDefault(u);
				return target is null
					? Task.FromResult<Result<RoomOutcome>>(RoomOutcome.TargetOffline)
					: r.Members.InviteAsync(target, ct);
			}));
		Command<UserRequest>(rooms, "addref", "Make a player a referee.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Authority.AddRefereeAsync(u, ct)));
		Command<UserRequest>(rooms, "removeref", "Remove a referee.",
			(r, b, s, ct) => WithUser(s, b.User, ct, u => r.Authority.RemoveRefereeAsync(u, ct)));

		Command<CountdownRequest>(rooms, "start", "Start the round, or start it after a countdown.", (r, b, _, ct) =>
			b.Seconds is { } seconds and > 0
				? Countdown(r, seconds, true, ct)
				: r.Rounds.StartAsync(ct));
		Command<CountdownRequest>(rooms, "timer", "Start a countdown of 30 seconds or the given length.",
			(r, b, _, ct) => Countdown(r, b.Seconds ?? 30, false, ct));
		Command<object?>(rooms, "aborttimer", "Cancel the running countdown.",
			(r, _, _, ct) => r.Rounds.CancelCountdownAsync(ct));
		Command<object?>(rooms, "abort", "Abort the round in progress.", (r, _, _, ct) => r.Rounds.AbortAsync(ct));
	}

	private static string Truncate(string name)
	{
		return name.Length > MaxRoomNameLength ? name[..MaxRoomNameLength] : name;
	}

	private static Task<Result<RoomOutcome>> Countdown(ScopedRoom room, int seconds, bool startsRound,
		CancellationToken ct)
	{
		return seconds < 1 || TimeSpan.FromSeconds(seconds) > ScopedRoomRounds.MaxCountdown
			? Task.FromResult<Result<RoomOutcome>>(RoomOutcome.InvalidSettings)
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

	/// <summary>Maps <c>POST /rooms/{id}/{name}</c> to a room operation of the scoped room.</summary>
	private static void Command<TBody>(RouteGroupBuilder rooms, string name, string summary,
		Func<ScopedRoom, TBody, IServiceProvider, CancellationToken, Task<Result<RoomOutcome>>> run)
	{
		rooms.MapPost("/{id:int}/" + name, async (HttpContext context, int id, Lobby lobby) =>
			{
				if (await Find(context, lobby, id) is not { } room) return Api.NotFound("No such room.");

				TBody? body = default;
				if (typeof(TBody) != typeof(object))
				{
					if (!context.Request.HasJsonContentType()) return Api.Invalid("A JSON body is required.");
					try
					{
						body = await context.Request.ReadFromJsonAsync<TBody>(Api.Json, context.RequestAborted);
					}
					catch (System.Text.Json.JsonException)
					{
						return Api.Invalid("The body is not valid.");
					}

					if (body is null) return Api.Invalid("A body is required.");
				}

				var result = await run(room.ScopeTo(context.Connection()), body!, context.RequestServices,
					context.RequestAborted);
				return result.Answer();
			}).WithSummary(summary).WithTags("Rooms");
	}

	private static async Task<Room?> Find(HttpContext context, Lobby lobby, int id)
	{
		return lobby.Find(id) is { } room &&
		       await lobby.ScopeTo(context.Connection()).CanViewAsync(room.Match, context.RequestAborted)
			? room
			: null;
	}

	private static UserRef Ref(UserRecord user)
	{
		return new UserRef(user.Id, user.Value.Name);
	}

	internal static RoomView ViewOf(Room room)
	{
		var host = room.Authority.Host;
		var settings = room.Settings.MatchSettings;
		return new RoomView(room.Id, room.Match.Id, room.Settings.Name, room.IsTournament, room.Settings.IsPrivate,
			room.Settings.Password.Length > 0, room.Slots.Locked, room.Rounds.InProgress, Ref(room.Authority.Creator),
			host is null ? null : Ref(host.Record), [.. room.Authority.Referees.Select(Ref)],
			[.. room.Members.Banned.Select(Ref)], room.Settings.Beatmap?.Id, settings.Mode, settings.Mods.ToString(),
			settings.Freemods, settings.TeamType, settings.WinCondition,
			[.. room.Slots.Select(slot => new SlotView(slot.Index, slot.Locked, slot.Status, slot.Team,
				slot.Mods?.ToString(), slot.Player is null ? null : Ref(slot.Player.Record),
				host is not null && slot.Player == host))]);
	}

	private static async Task<IResult> ListAsync(HttpContext context, Lobby lobby, int? offset, int? limit)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var scoped = lobby.ScopeTo(context.Connection());
		var visible = new List<Room>();
		foreach (var room in lobby.Rooms.Values.OrderBy(room => room.Id))
			if (await scoped.CanViewAsync(room.Match, context.RequestAborted))
				visible.Add(room);

		return Api.Paged(new Page<RoomView>([.. visible.Skip(page!.Offset).Take(page.Limit).Select(ViewOf)],
			visible.Count), page);
	}

	private static async Task<IResult> GetAsync(HttpContext context, Lobby lobby, int id)
	{
		return await Find(context, lobby, id) is { } room ? Api.Ok(ViewOf(room)) : Api.NotFound("No such room.");
	}

	private static async Task<IResult> LiveAsync(HttpContext context, Lobby lobby, int id)
	{
		if (await Find(context, lobby, id) is not { } room) return Api.NotFound("No such room.");

		string? last = null;
		return await context.StreamAsync($"room:{id}", _ => Task.FromResult<(object, string?)>((ViewOf(room), null)), null,
			_ =>
			{
				if (room.IsClosed) return Task.FromResult(new SsePoll([new SseEvent(new { type = "closed" })], true));

				var view = ViewOf(room);
				var json = System.Text.Json.JsonSerializer.Serialize(view, Api.Json);
				if (json == last) return Task.FromResult(new SsePoll([]));

				last = json;
				return Task.FromResult(new SsePoll([new SseEvent(new { type = "room", room = view })]));
			});
	}

	private static async Task<IResult> CreateAsync(HttpContext context, Lobby lobby, CreateRoomRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Name)) return Api.Invalid("name is required.");

		var (room, result) = await lobby.ScopeTo(context.Connection()).OpenRoomAsync(Truncate(request.Name),
			request.Password ?? string.Empty, true, request.IsPrivate ?? false,
			cancellationToken: context.RequestAborted);
		return result.IsOk ? Api.Created(ViewOf(room!)) : result.Refusal();
	}
}
