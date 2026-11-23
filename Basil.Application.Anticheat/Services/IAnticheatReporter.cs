using Basil.Application.Sessions.Objects;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Services;

public interface IAnticheatReporter
{
	OsuFlags Report(StableSession player, OsuFlags flags);
}