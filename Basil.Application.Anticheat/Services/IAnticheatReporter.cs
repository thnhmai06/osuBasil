using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Services;

public interface IAnticheatReporter
{
	OsuFlags Report(StableSession player, OsuFlags flags);
}