using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores for a page of the scores of the users and the beatmaps that a search names, newest first.</summary>
/// <param name="User">Only the scores of this user, named by id or by name, or <see langword="null" /> for any.</param>
/// <param name="BeatmapId">Only the scores on this beatmap, or <see langword="null" /> for any.</param>
/// <param name="Beatmaps">
///     Only the scores on the beatmaps this search finds (at most <see cref="ListLimits.Ceiling" /> of them), or
///     <see langword="null" /> for any.
/// </param>
/// <param name="Page">Which part of the listing to return.</param>
/// <remarks>
///     Scores are public. When both <paramref name="BeatmapId" /> and <paramref name="Beatmaps" /> are given, only the
///     scores on that beatmap that also match the search are returned.
/// </remarks>
public sealed record SearchScores(UserReference? User, int? BeatmapId, BeatmapQuery? Beatmaps, PageRequest Page)
	: IRequest<ScoreListing>;
