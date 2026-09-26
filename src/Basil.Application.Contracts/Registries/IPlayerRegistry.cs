using Basil.Application.Models.Sessions;

namespace Basil.Application.Contracts.Registries;

/// <summary>
///     The runtime, in-memory directory of every currently online session, lost on restart.
/// </summary>
/// <remarks>
///     Thread-safe. An account holds at most one session of a given concrete session type at a time.
/// </remarks>
public interface IPlayerRegistry
{
	/// <summary>Gets a snapshot of every currently registered session, keyed by user id.</summary>
	IReadOnlyDictionary<int, UserSession> AllById { get; }

	/// <summary>Finds the currently registered session for a username.</summary>
	/// <param name="name">The username to look up.</param>
	/// <returns>The matching session, or <see langword="null" /> when the user is not online.</returns>
	UserSession? FindByName(string name);

	/// <summary>Registers a session.</summary>
	/// <param name="session">The session to register.</param>
	/// <returns>
	///     <see langword="true" /> if the session was registered; <see langword="false" /> when the
	///     account already has a session of the same concrete type registered.
	/// </returns>
	bool TryAdd(UserSession session);

	/// <summary>Removes a session from the registry.</summary>
	/// <param name="session">The session to remove.</param>
	void Remove(UserSession session);
}