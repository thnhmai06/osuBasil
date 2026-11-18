using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Shared.Actors;

public interface IActor
{
	UserRecord Record { get; }

	UserPrivileges EffectivePrivileges(DateTimeOffset now);
}