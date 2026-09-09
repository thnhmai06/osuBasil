using System.Text.Json;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Bot;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Shared.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}/slots` read, reassign, kick, and invite routes.</summary>
internal static class MatchSlotEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match slot routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchSlots(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/slots", async (int matchId, IMatchRegistry matchRegistry,
				ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
				IUserRepository users, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				return Results.Json(
					await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry, users,
						cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchSlots")
			.WithSummary("Get match slots.")
			.WithDescription("""
			                 Returns the match's slots as `{ slots: [...] }`. Always 16 entries (index 1-16, matching `!mp move`'s convention); `user` is null when the slot is empty.

			                 For a live stream of the same data, use `GET /matches/{matchId}/slots/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/slots/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return SseEndpoints.NotLive();

				return MatchLiveRoutes.HandleSlots(context, match, events,
					() => match.SlotsSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchSlotsLive")
			.WithSummary("Stream match slots.")
			.WithDescription("""
			                 Server-Sent Events stream of the same data as `GET /matches/{matchId}/slots`.

			                 The first event is the full current list; later events carry only the fields that changed.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapPut("/matches/{matchId:numericid}/slots", (int matchId, ReplaceSlotsRequest body,
					IMatchRegistry matchRegistry,
					ISessionRegistry<GameSession> gameRegistry, ISessionRegistry<IrcSession> ircRegistry,
					IUserRepository users, MatchControlService matchControl,
					CancellationToken cancellationToken) =>
				HandleSlotsWrite(matchId, body.Slots, true, matchRegistry, gameRegistry, ircRegistry, users,
					matchControl, cancellationToken))
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceMatchSlots")
			.WithSummary("Replace match slots.")
			.WithDescription("""
			                 Reassigns the match's slots and returns the updated arrangement. `{ slots: [{ index, userId?, team?, locked? }, ...] }`.

			                 Every currently seated player's id must appear exactly once across the payload (reassignment/team/lock only; nobody may be silently added or dropped). Omitted `team` leaves that slot's existing team unchanged.

			                 Returns `400 Bad Request` if an entry sets both `userId` and `locked: true` or the same `userId` is assigned to more than one slot, `409 Conflict` if the payload's player set doesn't match the match's current occupants exactly or any `userId` isn't currently seated somewhere in this match, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("An entry cannot set both userId and locked: true."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("The payload's player set doesn't match this match's current occupants."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/matches/{matchId:numericid}/slots", async (int matchId, InviteRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, MatchControlService matchControl,
				MatchMembershipService matchMembership, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var userIds = body.UserIds;
				if (userIds.Count == 0) return Results.BadRequest(new ErrorResponse("userIds is required."));

				var results = new List<InviteResult>();
				var anySeated = false;

				// Force-leave any target's current match before touching this match's lock. Two match
				// locks are never held at once: this match's lock is acquired only after every old-match
				// leave below has already released its own lock (see MatchMembershipService.LeaveAsync's
				// lock-ownership contract).
				if (body.Force)
					foreach (var userId in userIds)
					{
						var target = gameRegistry.GetByUserId(userId);
						if (target?.Match is not { } oldMatch || oldMatch == match) continue;

						await using var oldMutation = await oldMatch.BeginMutationAsync(cancellationToken);

						await matchMembership.LeaveAsync(target, oldMatch, cancellationToken);
						oldMutation.PublishState();
					}

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var sender = (UserSession?)gameRegistry.GetByUserId(match.HostId) ??
					             ircRegistry.GetByUserId(match.HostId) ??
					             (UserSession?)gameRegistry.GetByUserId(BotBootstrapService.BotId) ??
					             ircRegistry.GetByUserId(BotBootstrapService.BotId);

					foreach (var userId in userIds)
					{
						var target = gameRegistry.GetByUserId(userId);
						if (target is null)
						{
							results.Add(new InviteResult(userId, false, "Not online with the osu! client."));
							continue;
						}

						if (body.Force)
						{
							var forceResult = await matchControl.ForceInviteAsync(match, target, cancellationToken);
							if (forceResult == MatchControlService.ForceInviteResult.Ok) anySeated = true;
							results.Add(forceResult switch
							{
								MatchControlService.ForceInviteResult.Ok => new InviteResult(userId, true, null),
								MatchControlService.ForceInviteResult.TargetBanned =>
									new InviteResult(userId, false, "Banned from this match."),
								MatchControlService.ForceInviteResult.TargetInAnotherMatch =>
									new InviteResult(userId, false, "Already in another match."),
								MatchControlService.ForceInviteResult.TargetIsBot =>
									new InviteResult(userId, false, "Cannot invite BasilBot."),
								_ => new InviteResult(userId, false, "No free slot.")
							});
							continue;
						}

						if (sender is null)
						{
							results.Add(
								new InviteResult(userId, false, "No session available to send the invite from."));
							continue;
						}

						var inviteResult = MatchControlService.Invite(sender, match, target);
						results.Add(inviteResult switch
						{
							MatchControlService.InviteResult.TargetAlreadyInRoom =>
								new InviteResult(userId, false, "Already in the room."),
							MatchControlService.InviteResult.TargetIsBot =>
								new InviteResult(userId, false, "Cannot invite BasilBot."),
							_ => new InviteResult(userId, true, null)
						});
					}

					if (anySeated) mutation.PublishState();
				}

				return Results.Json(results);
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("inviteMatchPlayers")
			.WithSummary("Invite players to a match.")
			.WithDescription("""
			                 Invites `{ userIds: int[], force }` to the match, returning one `{ userId, ok, error }` result per target.

			                 Without `force`, sends a standing invite (same as `!mp invite`): the target still needs to join themselves, subject to the room's password/private/lock gating. With `force: true`, bypasses password/private/lock and seats the target directly, moving them out of any other match they're currently in first. A banned target is still rejected regardless of `force`.

			                 A target moved out of another match is briefly in no match at all; if this match fills up in that window, they end up seated nowhere (`error: "No free slot."`) rather than back in their old room.

			                 Returns `200 OK` even if some targets failed.

			                 Returns `400 Bad Request` if `userIds` is empty, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<IReadOnlyList<InviteResult>>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new List<InviteResult>
			{
				new(9, true, null),
				new(21, false, "Banned from this match.")
			})
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userIds is required."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:numericid}/slots", async (int matchId, [FromBody] KickPlayerRequest body,
				IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
				ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
				MatchControlService matchControl, MatchMembershipService matchMembership,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				var targetUser = await users.FetchByIdAsync(body.UserId, cancellationToken);
				if (targetUser is null)
					return Results.BadRequest(new ErrorResponse("userId is not registered."));

				MatchControlService.KickResult result;
				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					result = await matchControl.KickAsync(null, null, match, targetUser.Id, targetUser.Name,
						cancellationToken);
					if (result is MatchControlService.KickResult.Ok) mutation.PublishState();
				}

				return result switch
				{
					MatchControlService.KickResult.TargetNotInMatch =>
						Results.BadRequest(new ErrorResponse("userId is not in this match.")),
					MatchControlService.KickResult.TargetIsReferee =>
						Results.BadRequest(new ErrorResponse("userId is a referee; remove referee status first.")),
					MatchControlService.KickResult.TargetIsBot =>
						Results.BadRequest(new ErrorResponse("userId is BasilBot and cannot be kicked.")),
					_ => await KickedResponseAsync()
				};

				async Task<IResult> KickedResponseAsync()
				{
					return Results.Json(await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry,
						users, cancellationToken));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("kickMatchPlayer")
			.WithSummary("Kick a player from a match.")
			.WithDescription("""
			                 Kicks the player identified by `{ userId }` and returns the resulting slot arrangement.

			                 Returns `400 Bad Request` if `userId` is not registered, not currently present in this match, is a referee (remove referee status first), or is BasilBot, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Slots")
			.Produces<MatchSlotsView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleSlots())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("userId is not in this match."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static MatchSlotsView SampleSlots()
	{
		var slots = new List<MatchSlotView>(16);
		for (var i = 0; i < 16; i++)
			slots.Add(new MatchSlotView(i + 1, null, SlotStatus.Open, null, null, null, null));
		slots[0] = new MatchSlotView(1, new UserBrief(7, "Alice", Country.Us), SlotStatus.NotReady, MatchTeam.Red,
			Mods.NoMod, false, false);
		slots[1] = new MatchSlotView(2, new UserBrief(9, "Carol", Country.Ca), SlotStatus.Ready, MatchTeam.Blue,
			Mods.NoMod, true, false);
		slots[15] = new MatchSlotView(16, null, SlotStatus.Locked, null, null, null, null);
		return new MatchSlotsView(slots);
	}

	/// <summary>
	///     Backs `PUT /matches/{matchId}/slots`: validates slot indexes, converts the body to patch
	///     entries, and applies them under <see cref="MatchSession.Lock" />, mapping
	///     <see cref="MatchControlService.SetSlotsAsync" /> results onto 200/400/409 responses.
	/// </summary>
	private static async Task<IResult> HandleSlotsWrite(int matchId, IReadOnlyList<SlotAssignment> slots,
		bool isFullReplace, IMatchRegistry matchRegistry, ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry, IUserRepository users,
		MatchControlService matchControl, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

		foreach (var slot in slots)
			if (slot.Index is < 1 or > 16)
				return Results.BadRequest(new ErrorResponse($"Slot index {slot.Index} is out of range (1-16)."));

		var entries = ToPatchEntries(slots);

		await using (var mutation = await match.BeginMutationAsync(cancellationToken))
		{
			var result = await matchControl.SetSlotsAsync(match, entries, isFullReplace, mutation, cancellationToken);
			return result switch
			{
				MatchControlService.SetSlotsResult.PlayerCountMismatch =>
					Results.Conflict(
						new ErrorResponse(
							"The payload's player set doesn't match this match's current occupants.")),
				MatchControlService.SetSlotsResult.UnknownUserId =>
					Results.Conflict(new ErrorResponse("A referenced userId is not currently seated in this match.")),
				MatchControlService.SetSlotsResult.DuplicateUserId =>
					Results.BadRequest(new ErrorResponse("A userId cannot be assigned to more than one slot.")),
				MatchControlService.SetSlotsResult.SlotOccupiedAndLocked =>
					Results.BadRequest(new ErrorResponse("An entry cannot set both userId and locked: true.")),
				_ => Results.Json(
					await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry, users,
						cancellationToken))
			};
		}
	}

	/// <summary>
	///     Turns a request's per-slot assignments into the team/lock map <see cref="MatchControlService" />
	///     consumes, translating each <see cref="MatchTeam" /> into the `"Red"`/`"Blue"` strings it
	///     expects (null for a neutral team), and each 1-based <see cref="SlotAssignment.Index" /> into
	///     the 0-based index the internal slot array uses.
	/// </summary>
	/// <param name="slots">The slot assignments from the request body.</param>
	private static IReadOnlyDictionary<int, MatchControlService.SlotPatchEntry> ToPatchEntries(
		IReadOnlyList<SlotAssignment> slots)
	{
		var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>();
		foreach (var slot in slots)
		{
			var team = slot.Team switch
			{
				MatchTeam.Red => "Red",
				MatchTeam.Blue => "Blue",
				_ => null
			};
			entries[slot.Index - 1] = new MatchControlService.SlotPatchEntry(slot.UserId, team, slot.Locked);
		}

		return entries;
	}
}

/// <summary>Request body for `DELETE /matches/{matchId}/slots`: kicks the seated player.</summary>
public sealed record KickPlayerRequest(int UserId);

/// <summary>
///     Request body for `POST /matches/{matchId}/slots`: one target per id, optionally forced
///     straight into the room.
/// </summary>
public sealed record InviteRequest(IReadOnlyList<int> UserIds, bool Force);

/// <summary>Per-target outcome returned by `POST /matches/{matchId}/slots`.</summary>
public sealed record InviteResult(int UserId, bool Ok, string? Error);

/// <summary>One per-slot entry in a <see cref="ReplaceSlotsRequest" />.</summary>
/// <param name="Index">The 1-based slot index (1 through 16), matching `!mp move`'s convention.</param>
/// <param name="UserId">The player id to assign, or <see langword="null" /> to leave the slot unassigned.</param>
/// <param name="Team">Either `"Red"` or `"Blue"`; any other value leaves the destination slot's existing team unchanged.</param>
/// <param name="Locked">Whether to lock the slot; cannot be combined with a non-null <see cref="UserId" />.</param>
public sealed record SlotAssignment(int Index, int? UserId = null, MatchTeam? Team = null, bool? Locked = null);

/// <summary>Request body for `PUT /matches/{matchId}/slots`: every seated player must appear exactly once.</summary>
public sealed record ReplaceSlotsRequest(IReadOnlyList<SlotAssignment> Slots);