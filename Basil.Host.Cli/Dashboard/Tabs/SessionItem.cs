using System.Globalization;
using Basil.Domain.Sessions;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed record SessionItem(
	long Id,
	int UserId,
	string UserName,
	ulong Mask,
	SessionType Type,
	string Ip,
	string Client,
	DateTimeOffset Started,
	DateTimeOffset LastActive,
	bool Live,
	string EndReason)
{
	public static readonly SessionType[] Kinds =
		[SessionType.Stable, SessionType.StableTourney, SessionType.Irc, SessionType.Api];

	public string Kind => KindName(Type);

	public string LastSeen => Live ? "Online" : Time(LastActive);

	public string Where => Client.Length == 0 ? Ip : $"{Client}  {Ip}";

	public static string KindName(SessionType type)
	{
		return type switch
		{
			SessionType.StableTourney => "Tourney",
			_ => type.ToString()
		};
	}

	public static string Time(DateTimeOffset moment)
	{
		return moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	}

	public static SessionItem Of(SessionRecord record, DateTimeOffset now)
	{
		return Of(new Session(record), now);
	}

	public static SessionItem Of(Session session, DateTimeOffset now)
	{
		var record = session.Record;
		var data = record.Value;
		var live = session.StatusAt(now) is not (SessionStatus.Expired or SessionStatus.Ended);
		return new SessionItem(record.Id, data.User.Id, data.User.Value.Name, (ulong)data.User.Value.Privileges,
			data.Type, data.Fingerprint.Ip.ToString(), data.Fingerprint.ClientInformation ?? string.Empty,
			data.StartedAt,
			session.Record.Value.LastActiveAt, live, session.Record.Value.Ended?.Reason.ToString() ?? string.Empty);
	}
}