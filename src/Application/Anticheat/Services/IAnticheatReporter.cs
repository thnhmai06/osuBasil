using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Services;

public interface IAnticheatReporter
{
	OsuFlags Report(StableConnection player, OsuFlags flags);
}