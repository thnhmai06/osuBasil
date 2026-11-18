using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record ListMatches(Session Actor, MatchQuery Query, PageRequest Page) : IRequest<MatchListing>;

public sealed record MatchListing(RoomOutcome Result, Page<MatchRecord>? Matches);