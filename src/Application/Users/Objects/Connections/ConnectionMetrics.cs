using System.Diagnostics.Metrics;
using Basil.Domain.Authentication;

namespace Basil.Application.Users.Objects.Connections;

/// <summary>The measurements about connections that Users publishes on the server's meter.</summary>
public static class ConnectionMetrics
{
	/// <summary>Gets the name of the instrument that counts refused connections.</summary>
	public const string RejectedName = "basil.connections.rejected";

	/// <summary>Gets the tag that names the kind of the refused connection.</summary>
	public const string TypeTag = "type";

	/// <summary>Gets the tag that names why the connection was refused.</summary>
	public const string ReasonTag = "reason";

	/// <summary>Gets the reason of a connection refused because its user holds too many of its kind.</summary>
	public const string TooManySessions = "too_many_sessions";

	/// <summary>Counts the connections refused, tagged with their kind and the reason.</summary>
	public static readonly Counter<long> Rejected = new Meter("Basil.Users", "1.0")
		.CreateCounter<long>(RejectedName, description: "Connections the server refused to open.");

	private static readonly long[,] Counts =
		new long[Enum.GetValues<SessionType>().Max(type => (int)type) + 1, Enum.GetValues<ConnectionCount>().Length];

	/// <summary>Gets how many connections of a kind have been counted for an event since the server started.</summary>
	/// <param name="type">The kind of connection.</param>
	/// <param name="kind">The event counted.</param>
	/// <returns>The running total; the bot's own connections are not counted.</returns>
	public static long Total(SessionType type, ConnectionCount kind)
	{
		return Interlocked.Read(ref Counts[(int)type, (int)kind]);
	}

	internal static void Record(SessionType type, ConnectionCount kind)
	{
		Interlocked.Increment(ref Counts[(int)type, (int)kind]);
	}
}

/// <summary>The connection events the server counts for each kind of connection.</summary>
public enum ConnectionCount
{
	/// <summary>A connection was opened.</summary>
	Opened,

	/// <summary>A connection was closed, whatever the reason.</summary>
	Closed,

	/// <summary>A connection was closed because its client sent nothing for too long.</summary>
	Timeout,

	/// <summary>A connection was closed because its transport was lost.</summary>
	Errors,

	/// <summary>A connection was refused because its user holds too many of its kind.</summary>
	Rejected
}
