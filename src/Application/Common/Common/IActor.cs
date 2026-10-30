using Basil.Domain.Users;

namespace Basil.Application.Common.Common;

/// <summary>Whoever acts on an object through a scope: a user's connection, as far as a scope needs to know it.</summary>
public interface IActor
{
	/// <summary>Gets the user on whose behalf the actor acts.</summary>
	UserRecord Record { get; }

	/// <summary>Gets what the user may do at a moment, with the restrictions that apply then.</summary>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	/// <returns>The effective permissions.</returns>
	UserPermissions EffectivePermissions(DateTimeOffset now);
}