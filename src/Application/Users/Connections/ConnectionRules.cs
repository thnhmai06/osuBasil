using Basil.Domain.Auth;

namespace Basil.Application.Users.Connections;

/// <summary>The rules about the connections of each kind one user may hold.</summary>
public static class ConnectionRules
{
	/// <param name="type">The kind of connection.</param>
	extension(ConnectionType type)
	{
		/// <summary>Gets a value that indicates whether one user may hold several connections of a kind at once.</summary>
		/// <returns><see langword="true" /> for IRC, osu!tourney and HTTP API connections.</returns>
		/// <remarks>Each IRC client of a user has a key of its own, so several can be connected at once.</remarks>
		public bool AllowsMany()
		{
			return type is ConnectionType.Tourney or ConnectionType.Irc or ConnectionType.Api;
		}

		/// <summary>Gets how long a connection of a kind may send nothing before it is closed.</summary>
		/// <returns>300 seconds for every client connection.</returns>
		internal TimeSpan IdleTimeout()
		{
			return TimeSpan.FromSeconds(300);
		}
	}
}
