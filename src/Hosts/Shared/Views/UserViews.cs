using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Users;

namespace Basil.Host.Shared.Views;

/// <summary>A user as another resource refers to them.</summary>
public sealed record UserRef(int Id, string Name)
{
	/// <summary>Describes an account.</summary>
	public static UserRef Of(UserRecord user)
	{
		return new UserRef(user.Id, user.Value.Name);
	}
}

/// <summary>An account as the API shows it.</summary>
public sealed record UserView(
	int Id,
	string Name,
	string Country,
	ulong Permissions,
	ulong EffectivePermissions,
	IReadOnlyList<RestrictionView>? Restrictions)
{
	/// <summary>Describes an account with the restrictions in force at the moment, when they are given.</summary>
	public static UserView Of(UserRegistry registry, UserRecord record, TimeProvider clock,
		IReadOnlyList<UserRestriction>? restrictions = null)
	{
		var user = registry.Of(record);
		var now = clock.GetUtcNow();
		return new UserView(record.Id, record.Value.Name, record.Value.Country.ToAcronym().ToLowerInvariant(),
			(ulong)record.Value.Permissions, (ulong)user.EffectivePermissions(now),
			restrictions is null
				? null
				: [.. restrictions.Where(r => r.Value.IsActive(now)).Select(RestrictionView.Of)]);
	}
}

/// <summary>A restriction as the API shows it.</summary>
public sealed record RestrictionView(int Id, ulong Permissions, DateTimeOffset StartsAt, DateTimeOffset? EndsAt)
{
	/// <summary>Describes a restriction.</summary>
	public static RestrictionView Of(UserRestriction r)
	{
		return new RestrictionView(r.Id, (ulong)r.Value.Permissions, r.Value.StartsAt, r.Value.EndsAt);
	}
}

/// <summary>One connection of a session as the API shows it.</summary>
public sealed record ConnectionView(
	string Ip,
	string? ClientInfo,
	DateTimeOffset OpenedAt,
	DateTimeOffset LastActiveAt,
	DateTimeOffset? ClosedAt,
	SessionEndReason? CloseReason)
{
	/// <summary>Describes a connection.</summary>
	public static ConnectionView Of(ConnectionRecord c)
	{
		var data = c.Value;
		return new ConnectionView(data.Ip.ToString(), data.ClientInfo, data.OpenedAt, data.LastActiveAt, data.ClosedAt,
			data.CloseReason);
	}
}

/// <summary>A session as the API shows it.</summary>
public sealed record SessionView(
	long Id,
	SessionType Type,
	string Ip,
	string? ClientInfo,
	DateTimeOffset StartedAt,
	DateTimeOffset LastActiveAt,
	DateTimeOffset? EndedAt,
	SessionEndReason? EndReason)
{
	/// <summary>Describes a session.</summary>
	public static SessionView Of(SessionRecord s)
	{
		var data = s.Value;
		var device = data.Fingerprint;
		return new SessionView(s.Id, data.Type, device.Ip.ToString(), device.ClientInfo, data.StartedAt,
			data.LastActiveAt, data.End?.At, data.End?.Reason);
	}
}