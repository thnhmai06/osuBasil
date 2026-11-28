using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record GetScore(int ScoreId) : IRequest<ScoreRecord?>;

public sealed record OpenReplay(int ScoreId) : IRequest<Stream?>;

public sealed record OpenReplayFrames(int ScoreId) : IRequest<Stream?>;