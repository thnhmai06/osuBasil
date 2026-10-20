using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Opens general channels and manages automatic channel membership.</summary>
public interface IChannelService : IEventPublisher<ChannelEvent>
{
	/// <summary>Opens a general channel.</summary>
	/// <param name="channel">The channel to open.</param>
	/// <returns>The open channel, or <see langword="null" /> when a channel with that name is already open.</returns>
	/// <remarks>Announces <see cref="ChannelOpened" />; channels owned by a session, connection or room are not announced.</remarks>
	GeneralChannelSession? Open(GeneralChannel channel);

	/// <summary>Creates and opens a general channel.</summary>
	/// <param name="channel">The channel to create.</param>
	/// <param name="by">The connection creating it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, or NameTaken when a channel with that name is already open.</returns>
	/// <remarks>
	///     Requires <see cref="Permissions.ServerConfigure" /> and announces <see cref="ChannelOpened" />.
	///     This operation will move to the Server object.
	/// </remarks>
	Task<ChannelManageResult> CreateAsync(GeneralChannel channel, Connection by,
		CancellationToken cancellationToken = default);

	/// <summary>Joins a connection to every open general channel configured for automatic joining.</summary>
	/// <param name="by">The connection that just opened.</param>
	void JoinAutoChannels(Connection by);

	/// <summary>Removes a connection from every open general channel.</summary>
	/// <param name="by">The connection that closed.</param>
	void PartAll(Connection by);
}
