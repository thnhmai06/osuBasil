using Basil.Application.Sessions.Objects;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Services;

public interface IAnticheatReporter
{
	ClientFlags Report(StableSession player, ClientFlags flags);
}