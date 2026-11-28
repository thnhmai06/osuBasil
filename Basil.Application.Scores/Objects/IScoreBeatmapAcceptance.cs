using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Scores.Objects;

public interface IScoreBeatmapAcceptance
{
	bool AcceptsUnknown(UserRecord player, Md5Hash beatmap);
}