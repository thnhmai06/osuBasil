using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Events;
using Basil.Domain.Multiplayer.Records;
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
	IRepository<int, User> usersById,
	IChannelRegistry channels,
	IEventDispatcher dispatcher,
	ILocalizer localizer)
{
	/// <summary>Handles <c>!mp make</c> and <c>!mp makeprivate</c>.</summary>
	public async Task<string> MakeAsync(UserSession sender, IReadOnlyList<string> args, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var name = args.Count > 0 ? string.Join(' ', args) : $"{sender.UserId}'s room";
		var creator = await usersById.LoadAsync(sender.UserId, cancellationToken);

		var room = await lobby.CreateRoomAsync(creator, name, string.Empty, cancellationToken: cancellationToken);

		if (isPrivate)
		{
			room.Match.IsVisible = false;
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
			var user = await usersById.LoadAsync(sender.UserId, cancellationToken);
			if (user is null) return localizer.Get(MpReplies.NoActiveMatchWithId, roomId);

			if (room.IsBanned(user))
				return localizer.Get(MpReplies.BannedFromMatch);

			if (!room.Match.IsVisible && !room.IsInvited(user) && !room.HasRefereePermission(user))
				return localizer.Get(MpReplies.PrivateRoomJoinDenied, roomId);

			if (!room.CheckPassword(password))
				return localizer.Get(MpReplies.IncorrectPassword);

			if (room.Join(user) is null)
				return localizer.Get(MpReplies.MatchIsFull);

			if (sender is GameSession game)
				game.RoomId = room.Id;

			if (channels.AllByName.TryGetValue(room.Channel.Name, out var channelSession))
			{
				channelSession.Join(room.Channel, user, sender);
				await FlushAsync(channelSession, cancellationToken);
			}

			return localizer.Get(MpReplies.JoinedMatch, room.Id, room.Match.Name);
		}
	}

	/// <summary>Handles <c>!mp close</c>. Runs outside the caller's room scope: it manages its own via <see cref="Lobby" />.</summary>
	public async Task<string> CloseAsync(int roomId, CancellationToken cancellationToken)
	{
		await lobby.CloseRoomAsync(roomId, cancellationToken);
		return localizer.Get(MpReplies.ClosedMatch);
	}

	private async Task FlushAsync(IHasDomainEvents subject, CancellationToken cancellationToken)
	{
		foreach (var domainEvent in subject.DomainEvents)
			await dispatcher.DispatchAsync(domainEvent, cancellationToken);
		subject.ClearDomainEvents();
	}
}