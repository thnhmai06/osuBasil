using Basil.Domain.Authentication;

namespace Basil.Application.Users.Objects.Connections;

/// <summary>The rules about the connections of each kind one user may hold.</summary>
public static class ConnectionRules
{
	/// <param name="type">The kind of connection.</param>
	extension(SessionType type)
	{
		/// <summary>Gets a value that indicates whether a new connection of a kind takes the place of the user's closed one in a channel.</summary>
		/// <returns><see langword="true" /> for osu! game clients and BasilBot's connection, which hold one place per user.</returns>
		public bool ReplacesOnJoin()
		{
			return type is SessionType.Stable or SessionType.Bot;
		}

		/// <summary>Gets how long a connection of a kind may send nothing before it is closed.</summary>
		/// <returns>300 seconds for every client connection.</returns>
		internal TimeSpan IdleTimeout()
		{
			return TimeSpan.FromSeconds(300);
		}
	}
}