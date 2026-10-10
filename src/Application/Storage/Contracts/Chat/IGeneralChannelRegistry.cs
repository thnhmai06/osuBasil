namespace Basil.Application.Storage.Contracts.Chat;

/// <summary>The configured chat channels that are open, looked up by name.</summary>
/// <remarks>Room, spectator and private-message channels belong to their owners and are not listed here.</remarks>
public interface IGeneralChannelRegistry
{
	/// <summary>Gets every open configured channel.</summary>
	IReadOnlyCollection<GeneralChannelSession> All { get; }

	/// <summary>Finds an open configured channel by name, ignoring case.</summary>
	/// <param name="name">The channel name, including its leading <c>#</c>.</param>
	/// <returns>The channel, or <see langword="null" /> when no configured channel has that name.</returns>
	GeneralChannelSession? Find(string name);

	/// <summary>Adds a configured channel.</summary>
	/// <param name="channel">The channel to add.</param>
	/// <returns>
	///     <see langword="true" /> if the channel was added; <see langword="false" /> when a channel with that name
	///     already exists.
	/// </returns>
	internal bool Add(GeneralChannelSession channel);

	/// <summary>Removes a configured channel.</summary>
	/// <param name="channel">The channel to remove.</param>
	/// <returns><see langword="true" /> if the channel was removed.</returns>
	internal bool Remove(GeneralChannelSession channel);
}