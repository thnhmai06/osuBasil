namespace Basil.Domain.Users;

/// <summary>
///     Well-known persistent user ids for server-owned identities, kept separate from any single
///     service so both application and domain code can reference them without a layering cycle.
/// </summary>
public static class SystemUserIds
{
	/// <summary>The persistent user id seeded for BasilBot.</summary>
	public const int BasilBot = 0;
}