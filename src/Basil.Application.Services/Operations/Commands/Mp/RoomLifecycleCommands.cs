using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations.Commands.Mp;

/// <summary>
///     Backs the <c>!mp</c> subcommands that create, join, or close a room, rather than mutating one
///     the sender is already in.
/// </summary>
public sealed class RoomLifecycleCommands(
	Lobby lobby,
	IRoomRegistry rooms,
	IRepository<int, Match> matches,
	ILocalizer localizer)
{
	/// <summary>Handles <c>!mp make</c> and <c>!mp makeprivate</c>.</summary>
	public async Task<string> MakeAsync(UserSession sender, IReadOnlyList<string> args, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var name = args.Count > 0 ? string.Join(' ', args) : $"{sender.User.Name}'s room";
		var creator = sender.User;

		var room = await lobby.CreateRoomAsync(creator, name, string.Empty, cancellationToken: cancellationToken);

		if (isPrivate)
		{
			room.IsVisible = false;
			await matches.SaveAsync(room.Match, cancellationToken);
		}

		return localizer.Get(MpReplies.CreatedMatch, room.Id, room.Match.Name, isPrivate ? " (private)" : "");
	}

	/// <summary>Handles <c>!mp join &lt;id&gt; [password]</c>.</summary>
	public async Task<string> JoinAsync(UserSession sender, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var roomId))
			return localizer.Get(MpReplies.JoinUsage);

		var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;

		if (await rooms.EnterAsync(roomId, cancellationToken) is not { } scope)
			return localizer.Get(MpReplies.NoActiveMatchWithId, roomId);

		await using (scope)
		{
			var room = scope.Room;
			var user = sender.User;

			if (sender is not GameSession game)
				return localizer.Get(MpReplies.JoinRequiresClient);

			if (room.Banned.Contains(user))
				return localizer.Get(MpReplies.BannedFromMatch);

			if (!room.Match.IsVisible && !room.Invited.Contains(user) && !room.IsReferee(user))
				return localizer.Get(MpReplies.PrivateRoomJoinDenied, roomId);

			if (!room.VerifyPassword(password))
				return localizer.Get(MpReplies.IncorrectPassword);

			if (game.Room is { } other && !ReferenceEquals(other, room))
				return localizer.Get(MpReplies.AlreadyInAnotherRoom);

			if (game.JoinRoom(room) is null)
				return localizer.Get(MpReplies.MatchIsFull);

			sender.Join(room.Channel);

			return localizer.Get(MpReplies.JoinedMatch, room.Id, room.Match.Name);
		}
	}

	/// <summary>Handles <c>!mp close</c>. Runs outside the caller's room scope: it manages its own via <see cref="Lobby" />.</summary>
	public async Task<string> CloseAsync(int roomId, CancellationToken cancellationToken)
	{
		await lobby.CloseRoomAsync(roomId, cancellationToken);
		return localizer.Get(MpReplies.ClosedMatch);
	}
}
