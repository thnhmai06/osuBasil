using System.Globalization;
using Basil.Domain.Authentication;
using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One session of an account, as the sessions table and the detail of a user show it.</summary>
/// <param name="Id">The session id.</param>
/// <param name="UserId">The id of the account that owns it.</param>
/// <param name="UserName">The name of the account that owns it.</param>
/// <param name="Mask">The privilege flags of the account, as a number, which colour its name.</param>
/// <param name="Type">The kind of client that opened it.</param>
/// <param name="Ip">The address it was opened from.</param>
/// <param name="Client">The client details or label, or empty.</param>
/// <param name="Started">When it started.</param>
/// <param name="LastActive">When it was last used.</param>
/// <param name="Live">Whether it is open now.</param>
/// <param name="EndReason">Why it ended, or empty while it is live.</param>
internal sealed record SessionItem(long Id, int UserId, string UserName, ulong Mask, SessionType Type, string Ip,
	string Client, DateTimeOffset Started, DateTimeOffset LastActive, bool Live, string EndReason)
{
	/// <summary>Gets the kinds of session in the order the table groups them.</summary>
	public static readonly SessionType[] Kinds =
		[SessionType.Stable, SessionType.StableTourney, SessionType.Irc, SessionType.Api];

	/// <summary>Gets the name of the kind of session.</summary>
	public string Kind => KindName(Type);

	/// <summary>Gets when the session was last seen, or <c>Online</c> while it is live.</summary>
	public string LastSeen => Live ? "Online" : Time(LastActive);

	/// <summary>Gets the client details and the address as one text.</summary>
	public string Where => Client.Length == 0 ? Ip : $"{Client}  {Ip}";

	/// <summary>Names a kind of session.</summary>
	public static string KindName(SessionType type)
	{
		return type switch
		{
			SessionType.StableTourney => "Tourney",
			_ => type.ToString()
		};
	}

	/// <summary>Writes a moment in the local time of the operator.</summary>
	public static string Time(DateTimeOffset moment)
	{
		return moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	}

	/// <summary>Describes a stored session.</summary>
	/// <param name="record">The stored session.</param>
	/// <param name="now">The moment that decides whether it has expired.</param>
	public static SessionItem Of(SessionRecord record, DateTimeOffset now)
	{
		var data = record.Value;
		var live = data.End is null && (data.ExpiresAt is null || data.ExpiresAt > now);
		return new SessionItem(record.Id, data.User.Id, data.User.Value.Name, (ulong)data.User.Value.Privileges,
			data.Type, data.Fingerprint.Ip.ToString(), data.Fingerprint.ClientInfo ?? string.Empty, data.StartedAt,
			data.LastActiveAt, live, data.End?.Reason.ToString() ?? string.Empty);
	}
}
