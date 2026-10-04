namespace Basil.Domain.Client;

/// <summary>
///     Represents the version an osu! client reports when it connects: a build date, an optional revision and a
///     release stream.
/// </summary>
public readonly record struct ClientVersion(DateOnly Date, int? Revision, ClientVersionStream Stream);

/// <summary>
///     Represents the release stream of an osu! client.
/// </summary>
public enum ClientVersionStream : byte
{
	/// <summary>The stable release stream.</summary>
	Stable,

	/// <summary>The beta release stream.</summary>
	Beta,

	/// <summary>The cutting-edge release stream.</summary>
	CuttingEdge,

	/// <summary>The tournament build stream.</summary>
	Tourney,

	/// <summary>The developer release stream.</summary>
	Dev
}
