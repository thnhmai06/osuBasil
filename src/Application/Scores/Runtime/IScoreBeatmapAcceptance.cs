using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Runtime;

/// <summary>Decides whether a score on a beatmap the server does not have is still taken.</summary>
public interface IScoreBeatmapAcceptance
{
	/// <summary>Tells whether a score on an unknown beatmap is taken from a player.</summary>
	/// <param name="player">The player who set the score.</param>
	/// <param name="beatmap">The hash of the beatmap the score was played on.</param>
	/// <returns><see langword="true" /> when the score is taken although the server does not have the beatmap.</returns>
	bool AcceptsUnknown(UserRecord player, Md5 beatmap);
}