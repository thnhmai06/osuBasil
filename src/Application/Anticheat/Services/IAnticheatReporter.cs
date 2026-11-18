using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Game.Client;

namespace Basil.Application.Anticheat.Services;

public interface IAnticheatReporter
{
	OsuFlags Report(StableConnection player, OsuFlags flags);
}