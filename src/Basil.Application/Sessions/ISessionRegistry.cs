using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>
///     The runtime, in-memory directory of every currently online session of a given concrete type,
///     lost on restart.
/// </summary>
/// <typeparam name="TSession">The concrete session type tracked by this registry.</typeparam>
/// <remarks>Thread-safe. A user holds at most one session of each concrete type at a time.</remarks>
public interface ISessionRegistry<TSession> where TSession : UserSession
{
	/// <summary>Gets a snapshot of every currently registered session, keyed by user.</summary>
	IReadOnlyDictionary<User, TSession> AllByUser { get; }

	/// <summary>Registers a session.</summary>
	/// <param name="session">The session to register.</param>
	/// <returns>
	///     <see langword="true" /> if the session was registered; <see langword="false" /> when the
	///     user already has a session of the same concrete type registered.
	/// </returns>
	bool TryAdd(TSession session);

	/// <summary>Removes a session from the registry.</summary>
	/// <param name="session">The session to remove.</param>
	void Remove(TSession session);
}
