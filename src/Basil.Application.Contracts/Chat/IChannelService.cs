using System.Threading.Channels;
using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Chat;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Contracts.Chat;

/// <summary>Opens and closes chat channels and lets connections join, part, post and spectate.</summary>
public interface IChannelService : IEventPublisher<ChannelEvent>
{
	/// <summary>Opens a general channel.</summary>
	/// <param name="channel">The channel to open.</param>
	/// <returns>The open channel, or <see langword="null" /> when a general channel with that name is already open.</returns>
	/// <remarks>Announces <see cref="ChannelOpened" />; channels owned by a session, a connection or a room exist with their owner and are not announced.</remarks>
	GeneralChannelSession? Open(GeneralChannel channel);

	/// <summary>Closes a channel, removing every member.</summary>
	/// <param name="channel">The channel to close.</param>
	/// <remarks>Each member is reported as parted by force, then <see cref="ChannelClosed" />. A general channel also stops being listed. Closing a closed channel does nothing.</remarks>
	void Close(ChannelSession channel);

	/// <summary>Adds a connection to a channel's members.</summary>
	/// <param name="channel">The channel to join.</param>
	/// <param name="by">The connection joining.</param>
	/// <returns>The outcome of the join.</returns>
	/// <remarks>A connection may join only a channel it may read. If the same user is a member through a closed connection of a kind that allows one connection, that connection is removed first as an ordinary part.</remarks>
	ChannelJoinResult Join(ChannelSession channel, Connection by);

	/// <summary>Removes a connection from a channel's members.</summary>
	/// <param name="channel">The channel to part.</param>
	/// <param name="by">The connection parting.</param>
	/// <returns>The outcome of the part.</returns>
	ChannelPartResult Part(ChannelSession channel, Connection by);

	/// <summary>Posts a message to a channel.</summary>
	/// <param name="channel">The channel to post to.</param>
	/// <param name="by">The connection posting.</param>
	/// <param name="text">The text of the message.</param>
	/// <param name="notice">Whether the message is a notice, which never triggers an automatic reply.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of the post.</returns>
	/// <remarks>A message longer than <see cref="ChannelSession.MaxMessageLength" /> characters is cut. A private message is a post into the recipient's private-message channel; the recipient's away message is sent back to the author.</remarks>
	Task<ChannelPostResult> PostAsync(ChannelSession channel, Connection by, string text, bool notice = false, CancellationToken cancellationToken = default);

	/// <summary>Joins a connection to every general channel joined automatically.</summary>
	/// <param name="by">The connection that just opened.</param>
	void JoinAutoChannels(Connection by);

	/// <summary>Removes a connection from every general channel.</summary>
	/// <param name="by">The connection that closed.</param>
	void PartAll(Connection by);

	/// <summary>Starts spectating an osu! client.</summary>
	/// <param name="host">The client being spectated.</param>
	/// <param name="by">The connection that starts spectating.</param>
	/// <returns>The outcome.</returns>
	/// <remarks>osu! clients, osu!tourney clients and BasilBot can spectate.</remarks>
	SpectateResult Spectate(BanchoConnection host, Connection by);

	/// <summary>Stops spectating.</summary>
	/// <param name="by">The spectator that stops.</param>
	/// <returns><see langword="true" /> if the connection was spectating.</returns>
	bool StopSpectating(Connection by);

	/// <summary>Reports that a spectator cannot watch, for example because it lacks the beatmap.</summary>
	/// <param name="by">The spectator reporting it.</param>
	/// <returns><see langword="true" /> if the connection was spectating.</returns>
	bool CantSpectate(Connection by);
}