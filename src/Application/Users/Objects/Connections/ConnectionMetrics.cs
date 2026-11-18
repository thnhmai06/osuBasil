using System.Diagnostics.Metrics;
using Basil.Domain.Authentication;

namespace Basil.Application.Users.Objects.Connections;

public static class ConnectionMetrics
{
	public const string RejectedName = "basil.connections.rejected";

	public const string TypeTag = "type";

	public const string ReasonTag = "reason";

	public const string TooManySessions = "too_many_sessions";

	public static readonly Counter<long> Rejected = new Meter("Basil.Users", "1.0")
		.CreateCounter<long>(RejectedName, description: "Connections the server refused to open.");

	private static readonly long[,] Counts =
		new long[Enum.GetValues<SessionType>().Max(type => (int)type) + 1, Enum.GetValues<ConnectionCount>().Length];

	public static long Total(SessionType type, ConnectionCount kind)
	{
		return Interlocked.Read(ref Counts[(int)type, (int)kind]);
	}

	internal static void Record(SessionType type, ConnectionCount kind)
	{
		Interlocked.Increment(ref Counts[(int)type, (int)kind]);
	}
}

public enum ConnectionCount
{
	Opened,

	Closed,

	Timeout,

	Errors,

	Rejected
}