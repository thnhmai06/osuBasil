using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Contracts.Scores;

/// <summary>Which scores a score listing includes.</summary>
/// <param name="Player">The user who set the scores, or <see langword="null" /> for any.</param>
/// <param name="BeatmapHash">The beatmap the scores were set on, or <see langword="null" /> for any.</param>
public sealed record ScoreQuery(User? Player = null, Md5? BeatmapHash = null);
