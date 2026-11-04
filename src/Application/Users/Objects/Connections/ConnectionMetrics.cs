using System.Diagnostics.Metrics;

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
	public static readonly Counter<long> Rejected = new Meter("Basil", "1.0")
		.CreateCounter<long>(RejectedName, description: "Connections the server refused to open.");
}
