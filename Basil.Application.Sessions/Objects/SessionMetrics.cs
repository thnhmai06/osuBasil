using System.Diagnostics.Metrics;
using Basil.Domain.Sessions;

namespace Basil.Application.Sessions.Objects;

public static class SessionMetrics
{
	public const string RejectedName = "basil.connections.rejected";

	public const string TypeTag = "type";

	public const string ReasonTag = "reason";

	public const string AlreadyOnline = "already_online";

	public static readonly Meter Meter = new("Basil.Sessions", "1.0");

	public static readonly Counter<long> Rejected = Meter
		.CreateCounter<long>(RejectedName, description: "Connections the server refused to open.");

	private static readonly long[,] Counts =
		new long[Enum.GetValues<SessionType>().Max(type => (int)type) + 1, Enum.GetValues<SessionCount>().Length];

	public static long Total(SessionType type, SessionCount kind)
	{
		return Interlocked.Read(ref Counts[(int)type, (int)kind]);
	}

	internal static void Record(SessionType type, SessionCount kind)
	{
		Interlocked.Increment(ref Counts[(int)type, (int)kind]);
	}
}

public enum SessionCount
{
	Opened,

	Closed,

	Timeout,

	Errors,

	Rejected
}