using Basil.Application.Users.Registry;
using Basil.Domain.Sessions;
using Basil.Domain.Users;

namespace Basil.Host.Shared.Views;

public sealed record UserRef(int Id, string Name)
{
	public static UserRef Of(UserRecord user)
	{
		return new UserRef(user.Id, user.Value.Name);
	}
}

public sealed record UserView(
	int Id,
	string Name,
	string Country,
	ulong Privileges,
	ulong EffectivePrivileges,
	DateTimeOffset? SilencedUntil)
{
	public static UserView Of(UserRegistry registry, UserRecord record, TimeProvider clock)
	{
		var user = registry.Of(record);
		var now = clock.GetUtcNow();
		return new UserView(record.Id, record.Value.Name, record.Value.Country.ToAcronym().ToLowerInvariant(),
			(ulong)record.Value.Privileges, (ulong)user.EffectivePrivileges(now),
			record.Value.IsSilenced(now) ? record.Value.SilencedEnd : null);
	}
}

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
	public static SessionView Of(SessionRecord record)
	{
		var data = record.Value;
		var device = data.Fingerprint;
		return new SessionView(record.Id, data.Type, device.Ip.ToString(), device.ClientInformation, data.StartedAt,
			data.LastActiveAt, data.Ended?.At, data.Ended?.Reason);
	}
}