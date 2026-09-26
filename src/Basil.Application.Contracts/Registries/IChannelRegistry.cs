using Basil.Application.Models.Sessions;

namespace Basil.Application.Contracts.Registries;

/// <summary>
///     The runtime, in-memory directory of every currently open channel's membership, lost on
///     restart.
/// </summary>
/// <remarks>Thread-safe. A channel name is registered at most once.</remarks>
public interface IChannelRegistry
{
	/// <summary>Gets a snapshot of every currently registered channel session, keyed by channel name.</summary>
	IReadOnlyDictionary<string, ChannelSession> AllByName { get; }

	/// <summary>Registers a channel session.</summary>
	/// <param name="channel">The channel session to register.</param>
	/// <returns>
	///     <see langword="true" /> if the channel was registered; <see langword="false" /> when a
	///     channel with the same name is already registered.
	/// </returns>
	bool TryAdd(ChannelSession channel);

	/// <summary>Removes a channel session from the registry.</summary>
	/// <param name="name">The name of the channel to remove.</param>
	void Remove(string name);
}