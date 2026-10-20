using Basil.Domain.Users;
using Basil.Application.Objects.Users;

namespace Basil.Application.Objects.Sessions;

/// <summary>The users who are online.</summary>
public interface IUserRegistry
{
	/// <summary>Gets every online user by its account.</summary>
	IReadOnlyDictionary<UserRecord, User> Online { get; }

	/// <summary>Finds the open connection a token identifies.</summary>
	/// <param name="token">The token the client presented.</param>
	/// <returns>The connection, or <see langword="null" /> when no open connection has that token.</returns>
	Connection? Find(string token);

	/// <summary>Finds the osu! client a connection is spectating.</summary>
	/// <param name="connection">The connection to look up.</param>
	/// <returns>The online osu! client being spectated, or <see langword="null" /> when the connection is not spectating.</returns>
	BanchoConnection? FindSpectating(Connection connection);

	/// <summary>Adds an online user.</summary>
	internal void Add(User user);

	/// <summary>Removes an online user; a different online instance of the same account is left in place.</summary>
	internal bool Remove(User user);

	/// <summary>Makes a connection findable by its token.</summary>
	internal void Index(Connection connection);

	/// <summary>Stops a connection being findable by its token; another connection with the same token is left in place.</summary>
	internal void Unindex(Connection connection);

	/// <summary>Enters the scope in which connections are opened and closed one at a time.</summary>
	internal Lock.Scope Enter();
}
