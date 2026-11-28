using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class FindOnlineUserHandler(SessionRegistry registry) : IRequestHandler<FindOnlineUser, UserSessions?>
{
	public ValueTask<UserSessions?> Handle(FindOnlineUser request, CancellationToken ct)
	{
		var name = UserData.TryNormalize(request.Name);
		return ValueTask.FromResult(name is null
			? null
			: registry.Online.Values.FirstOrDefault(user => user.Record.Value.NormalizedName == name));
	}
}

internal sealed class ListOnlineUsersHandler(SessionRegistry registry)
	: IRequestHandler<ListOnlineUsers, IReadOnlyList<UserSessions>>
{
	public ValueTask<IReadOnlyList<UserSessions>> Handle(ListOnlineUsers request, CancellationToken ct)
	{
		var online = registry.Online.Where(pair => pair.Value.HasPresence)
			.ToDictionary(pair => pair.Key.Id, pair => pair.Value);
		IReadOnlyList<UserSessions> found = request.Ids is null
			? [.. online.Values]
			: [.. request.Ids.Where(online.ContainsKey).Select(id => online[id])];
		return ValueTask.FromResult(found);
	}
}

internal sealed class FindStableClientHandler(SessionRegistry registry) : IRequestHandler<FindStableClient, StableSession?>
{
	public ValueTask<StableSession?> Handle(FindStableClient request, CancellationToken ct)
	{
		var user = registry.Online.FirstOrDefault(pair => pair.Key.Id == request.UserId && pair.Value.HasPresence);
		return ValueTask.FromResult(user.Key is null ? null : registry.OpenStableClient(user.Key));
	}
}
