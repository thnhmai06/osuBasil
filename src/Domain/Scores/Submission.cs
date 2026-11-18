using Basil.Domain.Client;
using Basil.Domain.Cryptography;

namespace Basil.Domain.Scores;

public sealed record Submission
{
	public required ScoreData Score { get; init; }

	public required OsuFlags Flags { get; init; }

	public required Md5 HashByClient { get; init; }
}