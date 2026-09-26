using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations.Commands.Mp;

/// <summary>
///     Backs the <c>!mp</c> subcommands that act on a room's players and slots: <c>move</c>,
///     <c>host</c>/<c>clearhost</c>, <c>team</c>, <c>kick</c>, <c>ban</c>, and <c>unban</c>.
/// </summary>
public sealed class SlotCommands(
	IRepository<int, User> usersById,
	IRepository<string, User> usersByName,
	ILocalizer localizer)
{
	/// <summary>Resolves a <c>!mp</c> target token (a user id or a username) to a registered user.</summary>
	public async Task<User?> ResolveAsync(string token, CancellationToken cancellationToken)
	{
		return int.TryParse(token, out var id)
			? await usersById.LoadAsync(id, cancellationToken)
			: await usersByName.LoadAsync(UserSafeName.Of(token), cancellationToken);
	}

	/// <summary>Handles <c>!mp move &lt;name/id&gt; &lt;slot 1-16&gt;</c>.</summary>
	public async Task<string> MoveAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 2 || !int.TryParse(args[1], out var slotNumber) || slotNumber is < 1 or > 16)
			return localizer.Get(MpReplies.MoveUsage);

		var target = await ResolveAsync(args[0], cancellationToken);
		if (target is null || room.Slots.Find(target) is null)
			return localizer.Get(MpReplies.UserNotInMatchOrUnregistered);

		try
		{
			room.MoveSlot(target, slotNumber - 1);
		}
		catch (InvalidOperationException)
		{
			return localizer.Get(MpReplies.DestinationSlotNotOpen);
		}

		return localizer.Get(MpReplies.MovedToSlot, target.Name, slotNumber);
	}

	/// <summary>Handles <c>!mp host &lt;name/id&gt;</c>.</summary>
	public async Task<string> HostAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.HostUsage);

		var target = await ResolveAsync(args[0], cancellationToken);
		if (target is null || room.Slots.Find(target) is null)
			return localizer.Get(MpReplies.UserNotInMatchOrUnregistered);

		room.TransferHost(target);
		return localizer.Get(MpReplies.ChangedMatchHost, target.Name);
	}

	/// <summary>Handles <c>!mp clearhost</c>.</summary>
	public string ClearHost(Room room)
	{
		room.TransferHost(null);
		return localizer.Get(MpReplies.ClearedMatchHost);
	}

	/// <summary>Handles <c>!mp team &lt;name/id&gt; &lt;red|blue&gt;</c>.</summary>
	public async Task<string> TeamAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 2 || !Enum.TryParse<GameTeam>(args[1], true, out var team) || team == GameTeam.Neutral)
			return localizer.Get(MpReplies.TeamUsage);

		var target = await ResolveAsync(args[0], cancellationToken);
		if (target is null || room.Slots.Find(target) is null)
			return localizer.Get(MpReplies.UserNotInMatchOrUnregistered);

		try
		{
			room.ChangeTeam(target, team);
		}
		catch (InvalidOperationException)
		{
			return localizer.Get(MpReplies.TeamUsage);
		}

		return localizer.Get(MpReplies.MovedToTeam, target.Name, team);
	}

	/// <summary>Handles <c>!mp kick &lt;name/id&gt;</c>.</summary>
	public async Task<string> KickAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.KickUsage);

		var target = await ResolveAsync(args[0], cancellationToken);
		if (target is null || room.Slots.Find(target) is null)
			return localizer.Get(MpReplies.UserNotInMatchOrUnregistered);

		if (room.IsReferee(target))
			return localizer.Get(MpReplies.CannotKickReferee, target.Name);

		room.Kick(target);
		return localizer.Get(MpReplies.KickedFromMatch, target.Name);
	}

	/// <summary>Handles <c>!mp ban &lt;name/id&gt;</c>.</summary>
	public async Task<string> BanAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.BanUsage);

		var target = await ResolveAsync(args[0], cancellationToken);
		if (target is null) return localizer.Get(MpReplies.UserNotRegistered);

		if (room.IsReferee(target))
			return localizer.Get(MpReplies.CannotBanReferee, target.Name);

		room.Ban(target);
		return localizer.Get(MpReplies.BannedPlayerFromMatch, target.Name);
	}

	/// <summary>Handles <c>!mp unban &lt;name/id&gt;</c>.</summary>
	public async Task<string> UnbanAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.UnbanUsage);

		var target = await ResolveAsync(args[0], cancellationToken);
		if (target is null || !room.IsBanned(target))
			return localizer.Get(MpReplies.NotBannedFromMatch, args[0]);

		room.Unban(target);
		return localizer.Get(MpReplies.UnbannedFromMatch, target.Name);
	}
}