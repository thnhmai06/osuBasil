using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to list the matches that were or are being played.</summary>
/// <param name="Actor">The connection that asks; the filter is narrowed to what it may view.</param>
/// <param name="Query">The filter to apply.</param>
/// <param name="Page">Which part of the matches to return.</param>
public sealed record ListMatches(Connection Actor, MatchQuery Query, PageRequest Page) : IRequest<MatchListing>;

/// <summary>The answer to <see cref="ListMatches" />.</summary>
/// <param name="Result">The outcome of the request.</param>
/// <param name="Matches">One part of the matches, newest first, or <see langword="null" /> when it was refused.</param>
public sealed record MatchListing(Result<RoomOutcome> Result, Page<MatchRecord>? Matches);
