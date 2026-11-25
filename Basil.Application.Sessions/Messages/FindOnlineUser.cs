using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Asks for the online user with a name, compared the way user names are compared; the answer is <see langword="null"/> when no such user is online.</summary>
/// <param name="Name">The name of the user.</param>
public sealed record FindOnlineUser(string Name) : IRequest<UserSessions?>;

/// <summary>Asks for the online users who show a presence, optionally only some of them.</summary>
/// <param name="Ids">The ids to look for, or <see langword="null"/> for every such user; the answer keeps their order and leaves out an id nobody online has.</param>
public sealed record ListOnlineUsers(IReadOnlyList<int>? Ids) : IRequest<IReadOnlyList<UserSessions>>;

/// <summary>Asks for the open osu! client of an online user who shows a presence; the answer is <see langword="null"/> when there is none.</summary>
/// <param name="UserId">The id of the user.</param>
public sealed record FindStableClient(int UserId) : IRequest<StableSession?>;
