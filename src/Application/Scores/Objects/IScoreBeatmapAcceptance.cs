
using Basil.Domain.Users;

namespace Basil.Application.Scores.Objects;

public interface IScoreBeatmapAcceptance
{
	bool AcceptsUnknown(UserRecord player, Md5 beatmap);
}