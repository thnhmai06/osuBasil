using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Opens, creates, configures and deletes the general channels.</summary>
public interface IGeneralChannelService
{
	/// <summary>Opens a general channel.</summary>
	/// <param name="channel">The channel to open.</param>
	/// <returns>The open channel, or <see langword="null" /> when a general channel with that name is already open.</returns>
	/// <remarks>
	///     Announces <see cref="ChannelOpened" />; channels owned by a session, a connection or a room exist with their
	///     owner and are not announced.
	/// </remarks>
	GeneralChannelSession? Open(GeneralChannel channel);

	/// <summary>Creates a general channel and opens it.</summary>
	/// <param name="channel">The channel to create.</param>
	/// <param name="by">The connection creating it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, or NameTaken when a general channel with that name is open.</returns>
	/// <remarks>Requires <see cref="Permissions.DeveloperConfigureServer" />; announces <see cref="ChannelOpened" />.</remarks>
	Task<ChannelManageResult> CreateAsync(GeneralChannel channel, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Changes the settings of an open general channel.</summary>
	/// <param name="channel">The channel to change.</param>
	/// <param name="change">The settings to change.</param>
	/// <param name="by">The connection changing it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, or Closed when the channel has closed.</returns>
	/// <remarks>
	///     Requires <see cref="Permissions.DeveloperConfigureServer" />. Members who may no longer read the channel are
	///     removed; one <see cref="ChannelConfigured" /> reports the change and the removed members.
	/// </remarks>
	/// <exception cref="ArgumentException">A permission or the topic in <paramref name="change" /> is invalid.</exception>
	Task<ChannelManageResult> ConfigureAsync(GeneralChannelSession channel, GeneralChannelChange change, Connection by,
		CancellationToken cancellationToken = default);

	/// <summary>Closes a general channel and deletes it.</summary>
	/// <param name="channel">The channel to delete.</param>
	/// <param name="by">The connection deleting it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok or NotAuthorized.</returns>
	/// <remarks>Requires <see cref="Permissions.DeveloperConfigureServer" />; announces <see cref="ChannelClosed" />.</remarks>
	Task<ChannelManageResult> DeleteAsync(GeneralChannelSession channel, Connection by, CancellationToken cancellationToken = default);
}
