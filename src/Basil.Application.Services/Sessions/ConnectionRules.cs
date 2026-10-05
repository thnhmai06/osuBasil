using Basil.Application.Storage.Sessions;

namespace Basil.Application.Services.Sessions;

/// <summary>The rules about the connections of each kind one user may hold.</summary>
internal static class ConnectionRules
{
	/// <summary>Gets a value that indicates whether one user may hold several connections of a kind at once.</summary>
	/// <param name="type">The kind of connection.</param>
	/// <returns><see langword="true" /> for osu!tourney and HTTP API connections.</returns>
	internal static bool AllowsMany(this ConnectionType type)
	{
		return type is ConnectionType.Tourney or ConnectionType.Api;
	}

	/// <summary>Gets how long a connection of a kind may send nothing before it is closed.</summary>
	/// <param name="type">The kind of connection.</param>
	/// <returns>2 hours for an HTTP API connection; 300 seconds for any other.</returns>
	internal static TimeSpan IdleTimeout(this ConnectionType type)
	{
		return type is ConnectionType.Api ? TimeSpan.FromHours(2) : TimeSpan.FromSeconds(300);
	}
}