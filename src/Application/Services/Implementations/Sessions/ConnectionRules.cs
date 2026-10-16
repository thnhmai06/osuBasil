using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>The rules about the connections of each kind one user may hold.</summary>
internal static class ConnectionRules
{
	/// <param name="type">The kind of connection.</param>
	extension(ConnectionType type)
	{
		/// <summary>Gets a value that indicates whether one user may hold several connections of a kind at once.</summary>
		/// <returns><see langword="true" /> for osu!tourney and HTTP API connections.</returns>
		internal bool AllowsMany()
		{
			return type is ConnectionType.Tourney or ConnectionType.Api;
		}

		/// <summary>Gets how long a connection of a kind may send nothing before it is closed.</summary>
		/// <returns>2 hours for an HTTP API connection; 300 seconds for any other.</returns>
		internal TimeSpan IdleTimeout()
		{
			return type is ConnectionType.Api ? TimeSpan.FromHours(2) : TimeSpan.FromSeconds(300);
		}
	}
}