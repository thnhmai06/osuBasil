using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores for one score.</summary>
/// <param name="ScoreId">The id of the score.</param>
/// <remarks>Scores are public; the answer is <see langword="null" /> when no score has the id.</remarks>
public sealed record GetScore(int ScoreId) : IRequest<ScoreRecord?>;

/// <summary>Asks Scores to open the replay of a score.</summary>
/// <param name="ScoreId">The id of the score.</param>
/// <remarks>
///     The answer is <see langword="null" /> when no score has the id or it has no replay; the receiver disposes the
///     stream.
/// </remarks>
public sealed record OpenReplay(int ScoreId) : IRequest<Stream?>;

/// <summary>Asks Scores to open the frames of the replay of a score, as an osu! client downloads them.</summary>
/// <param name="ScoreId">The id of the score.</param>
/// <remarks>
///     The answer is <see langword="null" /> when no score has the id or it has no replay; the receiver disposes the
///     stream.
/// </remarks>
public sealed record OpenReplayFrames(int ScoreId) : IRequest<Stream?>;