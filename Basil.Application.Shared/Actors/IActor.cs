using Basil.Domain.Users;

namespace Basil.Application.Shared.Actors;

public interface IActor
{
	UserRecord Record { get; }

	UserPrivileges EffectivePrivileges(DateTimeOffset now);

	/// <summary>Whether privileges <paramref name="granted"/> may act on an account that holds <paramref name="target"/>: a strict superset.</summary>
	bool Outranks(UserPrivileges granted, UserPrivileges target)
	{
		return granted.Allows(target) && granted != target;
	}
}