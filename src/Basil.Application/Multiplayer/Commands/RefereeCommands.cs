using Basil.Application.Common;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
namespace Basil.Application.Multiplayer.Commands;

/// <summary>
///     Backs the <c>!mp</c> subcommands that manage invitations and referee status: <c>invite</c>,
///     <c>addref</c>, <c>removeref</c>, <c>listrefs</c>, and <c>banlist</c>.
/// </summary>
public sealed class RefereeCommands(SlotCommands targets, ISessionRegistry<GameSession> games, ILocalizer localizer)
{
	/// <summary>Handles <c>!mp invite &lt;name&gt;</c>.</summary>
	public async Task<string> InviteAsync(UserSession sender, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.InviteUsage);

		var target = await targets.ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotFound);

		if (!games.AllByUser.ContainsKey(target))
			return localizer.Get(MpReplies.InviteRequiresClient);

		if (room.Slots.Find(target) is not null)
			return localizer.Get(MpReplies.UserAlreadyInRoom);

		room.Invite(target);

		return localizer.Get(MpReplies.InvitedToRoom, target.Value.Name);
	}

	/// <summary>Handles <c>!mp addref &lt;name&gt;</c>.</summary>
	public async Task<string> AddRefAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.AddRefUsage);

		var target = await targets.ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotFound);

		if (room.IsReferee(target))
			return localizer.Get(MpReplies.TargetIsAlreadyAReferee, target.Value.Name);

		room.AddReferee(target);
		return localizer.Get(MpReplies.AddedReferee, target.Value.Name);
	}

	/// <summary>Handles <c>!mp removeref &lt;name&gt;</c>.</summary>
	public async Task<string> RemoveRefAsync(Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.RemoveRefUsage);

		var target = await targets.ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotFound);

		if (room.Creator is not null && room.Creator.Equals(target))
			return localizer.Get(MpReplies.CannotRemoveCreator, target.Value.Name);

		if (!room.IsReferee(target))
			return localizer.Get(MpReplies.TargetIsNotAReferee, target.Value.Name);

		room.RemoveReferee(target);
		return localizer.Get(MpReplies.RemovedReferee, target.Value.Name);
	}

	/// <summary>Handles <c>!mp listrefs</c>.</summary>
	public string ListReferees(Room room)
	{
		return room.Referees.Count == 0
			? localizer.Get(MpReplies.NoReferees)
			: $"{localizer.Get(MpReplies.MatchReferees)} {string.Join(", ", room.Referees.Select(r => r.Value.Name))}";
	}

	/// <summary>Handles <c>!mp banlist</c>.</summary>
	public string BanList(Room room)
	{
		var banned = room.Slots
			.Where(s => s.Session is not null)
			.Select(s => s.Session!.User)
			.Where(room.Banned.Contains)
			.ToList();

		return banned.Count == 0
			? localizer.Get(MpReplies.NoBannedPlayers)
			: $"{localizer.Get(MpReplies.MatchBans)} {string.Join(", ", banned.Select(u => u.Value.Name))}";
	}
}
