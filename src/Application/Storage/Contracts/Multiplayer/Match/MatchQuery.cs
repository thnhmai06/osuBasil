namespace Basil.Application.Storage.Contracts.Multiplayer.Match;

/// <summary>Which matches a match listing includes.</summary>
/// <param name="Ended">
///     Whether only ended (<see langword="true" />) or only running (<see langword="false" />) matches are
///     included, or <see langword="null" /> for both.
/// </param>
/// <param name="IncludePrivate">Whether matches with private history are included.</param>
public sealed record MatchQuery(bool? Ended = null, bool IncludePrivate = false);