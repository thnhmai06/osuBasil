using Basil.Domain.Users;

namespace Basil.Application.Storage.Contracts.Sessions;

/// <summary>The users who are online, each with their session.</summary>
public interface IUserRegistry
{
	/// <summary>Gets every online session by its user.</summary>
	IReadOnlyDictionary<User, UserSession> Sessions { get; }

	/// <summary>Finds the open connection a token identifies.</summary>
	/// <param name="token">The token the client presented.</param>
	/// <returns>The connection, or <see langword="null" /> when no open connection has that token.</returns>
	Connection? Find(string token);

	/// <summary>Finds the osu! client a connection is spectating.</summary>
	/// <param name="connection">The connection to look up.</param>
	/// <returns>The online osu! client being spectated, or <see langword="null" /> when the connection is not spectating.</returns>
	BanchoConnection? FindSpectating(Connection connection);

	/// <summary>Adds an online session.</summary>
	internal void Add(UserSession session);

	/// <summary>Removes an online session; a different session of the same user is left in place.</summary>
	internal bool Remove(UserSession session);

	/// <summary>Makes a connection findable by its token.</summary>
	internal void Index(Connection connection);

	/// <summary>Stops a connection being findable by its token; another connection with the same token is left in place.</summary>
	internal void Unindex(Connection connection);

	/// <summary>Enters the scope in which connections are opened and closed one at a time.</summary>
	internal Lock.Scope Enter();
}