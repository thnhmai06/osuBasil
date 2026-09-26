using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Application.Contracts.Registries;

/// <summary>
///     The runtime, in-memory directory of every currently open multiplayer room, lost on restart.
/// </summary>
/// <remarks>Thread-safe. A room may only be mutated through the exclusive scope <see cref="EnterAsync" /> grants.</remarks>
public interface IRoomRegistry
{
	/// <summary>Gets a read-only snapshot of every currently registered room, keyed by room id.</summary>
	IReadOnlyDictionary<int, Room> AllById { get; }

	/// <summary>Registers a room.</summary>
	/// <param name="room">The room to register.</param>
	/// <returns>
	///     <see langword="true" /> if the room was registered; <see langword="false" /> when a room
	///     with the same id is already registered.
	/// </returns>
	bool TryAdd(Room room);

	/// <summary>Removes a room from the registry.</summary>
	/// <param name="roomId">The id of the room to remove.</param>
	void Remove(int roomId);

	/// <summary>
	///     Waits for and grants exclusive permission to mutate a room until the returned scope is
	///     disposed.
	/// </summary>
	/// <param name="roomId">The id of the room to enter.</param>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>
	///     A scope holding the room, or <see langword="null" /> when no room with <paramref name="roomId" />
	///     is registered. Disposing a granted scope dispatches every domain event the room (and its
	///     slots) recorded while the scope was open, clears them, and only then releases the room for
	///     the next caller.
	/// </returns>
	Task<IRoomScope?> EnterAsync(int roomId, CancellationToken cancellationToken = default);
}

/// <summary>
///     Grants exclusive permission to mutate a <see cref="Room" /> for as long as the scope is held.
/// </summary>
public interface IRoomScope : IAsyncDisposable
{
	/// <summary>Gets the room this scope grants exclusive access to.</summary>
	Room Room { get; }
}