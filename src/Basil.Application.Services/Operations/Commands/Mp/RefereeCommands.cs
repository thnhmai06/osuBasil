using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Registries;
using Basil.Application.Models.Notifications;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Application.Services.Operations.Commands.Mp;

/// <summary>
///     Backs the <c>!mp</c> subcommands that manage invitations and referee status: <c>invite</c>,
///     <c>addref</c>, <c>removeref</c>, <c>listrefs</c>, and <c>banlist</c>.
/// </summary>
public sealed class RefereeCommands(SlotCommands targets, IPlayerRegistry players, ILocalizer localizer)
{
	/// <summary>Handles <c>!mp invite &lt;name&gt;</c>.</summary>
	public async Task<string> InviteAsync(UserSession sender, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.InviteUsage);

		var target = await targets.ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotFound);

		if (players.AllById.GetValueOrDefault(target.Id) is not GameSession)
			return localizer.Get(MpReplies.InviteRequiresClient);

		if (room.Slots.Find(target) is not null)
			return localizer.Get(MpReplies.UserAlreadyInRoom);

		room.Invite(target);
		if (players.AllById.GetValueOrDefault(target.Id) is { } targetSession)
			targetSession.Notify(new Invited(room, sender.UserId));

		return localizer.Get(MpReplies.InvitedToRoom, target.Name);
	}

	/// <summary>Handles <c>!mp addref &lt;name&gt;</c>.</summary>
	public async Task<string> AddRefAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.AddRefUsage);

		var target = await targets.ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotFound);

		if (room.IsReferee(target))
			return localizer.Get(MpReplies.TargetIsAlreadyAReferee, target.Name);

		room.AddReferee(target);
		return localizer.Get(MpReplies.AddedReferee, target.Name);
	}

	/// <summary>Handles <c>!mp removeref &lt;name&gt;</c>.</summary>
	public async Task<string> RemoveRefAsync(Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.RemoveRefUsage);

		var target = await targets.ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotFound);

		if (room.IsCreator(target))
			return localizer.Get(MpReplies.CannotRemoveCreator, target.Name);

		if (!room.IsReferee(target))
			return localizer.Get(MpReplies.TargetIsNotAReferee, target.Name);

		room.RemoveReferee(target);
		return localizer.Get(MpReplies.RemovedReferee, target.Name);
	}

	/// <summary>Handles <c>!mp listrefs</c>.</summary>
	public string ListReferees(Room room)
	{
		return room.Referees.Count == 0
			? localizer.Get(MpReplies.NoReferees)
			: $"{localizer.Get(MpReplies.MatchReferees)} {string.Join(", ", room.Referees.Select(r => r.Name))}";
	}

	/// <summary>Handles <c>!mp banlist</c>.</summary>
	public string BanList(Room room)
	{
		var banned = room.Slots.Where(s => s.User is not null).Select(s => s.User!)
			.Where(room.IsBanned).ToList();

		return banned.Count == 0
			? localizer.Get(MpReplies.NoBannedPlayers)
			: $"{localizer.Get(MpReplies.MatchBans)} {string.Join(", ", banned.Select(u => u.Name))}";
	}
}