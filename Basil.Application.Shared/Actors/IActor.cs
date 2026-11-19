using Basil.Domain.Users;

namespace Basil.Application.Shared.Actors;

public interface IActor
{
	UserRecord Record { get; }

	UserPrivileges EffectivePrivileges(DateTimeOffset now);
}