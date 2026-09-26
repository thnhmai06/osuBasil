using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations.Commands.Mp;

/// <summary>
///     Routes a <c>!mp</c> command to the group of subcommand classes that implements it.
/// </summary>
/// <remarks>
///     <c>make</c>, <c>makeprivate</c>, <c>join</c>, and <c>close</c> run outside a room scope: they
///     either have no room yet, or manage their own scope through <see cref="Lobby" />. Every other
///     subcommand resolves the sender's current room (<see cref="GameSession.RoomId" />) and runs
///     inside the single <see cref="IRoomRegistry.EnterAsync" /> scope <see cref="IRoomRegistry" />
///     grants for it, so its recorded domain events are dispatched exactly once, when the scope is
///     disposed.
/// </remarks>
public sealed class MpCommands(
	RoomLifecycleCommands lifecycle,
	RoomSettingsCommands settings,
	SlotCommands slots,
	RefereeCommands referees,
	MatchFlowCommands flow,
	IRoomRegistry rooms,
	IRepository<int, User> usersById,
	ILocalizer localizer)
{
	private static readonly string HelpText = string.Join('\n',
		"!mp settings - show match id, map, team type, win condition, mods, and slots",
		"!mp lock/unlock - lock or unlock the room", "!mp private [0|1] - show or set the room's private status",
		"!mp size <1-16> - set the number of available slots",
		"!mp move <name/id> <slot 1-16> - move a player to another slot",
		"!mp host <name/id> / clearhost - transfer or clear host", "!mp name <text> - rename the match",
		"!mp password [text] - set or clear the room password", "!mp invite <name> - invite an online player",
		"!mp addref/removeref <name> - grant or revoke referee status (creator only)",
		"!mp listrefs / banlist - list referees or banned players",
		"!mp team <name> <red|blue> - assign a player's team", "!mp map <beatmap id> - change the selected map",
		"!mp mods <mods>|Freemod|None - set the match mods",
		"!mp set <teammode 0-3> [scoremode 0-3] [size 1-16] - set team type, win condition, and size",
		"!mp start [seconds] / timer [seconds] / aborttimer - start now, after a countdown, or cancel one",
		"!mp abort - abort the round in progress", "!mp kick/ban/unban <name/id> - remove, block, or unblock a player",
		"!mp close - close the match", "!mp make/makeprivate <name> - create a tournament room",
		"!mp join <id> [password] - join a match by id");

	/// <summary>Runs a <c>!mp</c> command's subcommand, returning its reply.</summary>
	/// <param name="sender">The session issuing the command.</param>
	/// <param name="args">The command's argument tokens; <c>args[0]</c> is the subcommand name.</param>
	/// <param name="cancellationToken">A token that cancels the command.</param>
	public async Task<string> ExecuteAsync(UserSession sender, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var subcommand = args.Count > 0 ? args[0].ToLowerInvariant() : "";
		var subArgs = args.Skip(1).ToArray();

		switch (subcommand)
		{
			case "" or "help":
				return HelpText;
			case "make":
				return await lifecycle.MakeAsync(sender, subArgs, false, cancellationToken);
			case "makeprivate":
				return await lifecycle.MakeAsync(sender, subArgs, true, cancellationToken);
			case "join":
				return await lifecycle.JoinAsync(sender, subArgs, cancellationToken);
		}

		if (sender is not GameSession { RoomId: { } roomId })
			return localizer.Get(MpReplies.NotInARoom);

		if (subcommand == "close")
		{
			if (rooms.AllById.TryGetValue(roomId, out var current))
			{
				var actor = await usersById.LoadAsync(sender.UserId, cancellationToken);
				if (actor is null || !current.HasRefereePermission(actor))
					return localizer.Get(MpReplies.NotARefereeOfMatch, roomId);
			}

			return await lifecycle.CloseAsync(roomId, cancellationToken);
		}

		if (await rooms.EnterAsync(roomId, cancellationToken) is not { } scope)
			return localizer.Get(MpReplies.NotInARoom);

		await using (scope)
		{
			var room = scope.Room;
			var user = await usersById.LoadAsync(sender.UserId, cancellationToken);
			if (user is null) return localizer.Get(MpReplies.NotInARoom);

			var isReadOnly = subcommand is "settings" or "listrefs" or "banlist" ||
			                 (subcommand == "private" && subArgs.Length == 0);
			if (!isReadOnly && !room.HasRefereePermission(user))
				return localizer.Get(MpReplies.NotARefereeOfMatch, room.Id);

			if (subcommand is "addref" or "removeref" && !room.IsCreator(user))
				return localizer.Get(MpReplies.CreatorOnlyMp, $"!mp {subcommand}");

			return await DispatchAsync(sender, room, subcommand, subArgs, cancellationToken);
		}
	}

	private async Task<string> DispatchAsync(UserSession sender, Room room, string subcommand, string[] args,
		CancellationToken cancellationToken)
	{
		return subcommand switch
		{
			"settings" => await settings.SettingsAsync(room, cancellationToken),
			"lock" => settings.SetLocked(room, true),
			"unlock" => settings.SetLocked(room, false),
			"private" => settings.Private(room, args),
			"size" => settings.Resize(room, args),
			"move" => await slots.MoveAsync(room, args, cancellationToken),
			"host" => await slots.HostAsync(room, args, cancellationToken),
			"clearhost" => slots.ClearHost(room),
			"name" => settings.Rename(room, args),
			"password" => settings.ChangePassword(room, args),
			"invite" => await referees.InviteAsync(sender, room, args, cancellationToken),
			"addref" => await referees.AddRefAsync(room, args, cancellationToken),
			"removeref" => await referees.RemoveRefAsync(room, args, cancellationToken),
			"listrefs" => referees.ListReferees(room),
			"banlist" => referees.BanList(room),
			"team" => await slots.TeamAsync(room, args, cancellationToken),
			"set" => flow.Set(room, args),
			"map" => await flow.MapAsync(room, args, cancellationToken),
			"mods" => flow.SetMods(room, args),
			"start" => await flow.StartAsync(room, args, cancellationToken),
			"timer" => flow.Timer(room, args),
			"aborttimer" => flow.AbortTimer(room),
			"abort" => flow.Abort(room),
			"kick" => await slots.KickAsync(room, args, cancellationToken),
			"ban" => await slots.BanAsync(room, args, cancellationToken),
			"unban" => await slots.UnbanAsync(room, args, cancellationToken),
			_ => localizer.Get(MpReplies.UnknownMpSubcommand, subcommand)
		};
	}
}