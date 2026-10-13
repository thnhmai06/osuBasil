namespace Basil.Application.Storage.Contracts.Chat;

/// <summary>The configured chat channels that are open, looked up by name.</summary>
/// <remarks>Room, spectator and private-message channels belong to their owners and are not listed here.</remarks>
public interface IGeneralChannelRegistry
{
	/// <summary>Gets every open configured channel.</summary>
	IReadOnlyDictionary<string, GeneralChannelSession> All { get; }

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