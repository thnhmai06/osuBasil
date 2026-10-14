using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Joins connections to chat channels and parts them.</summary>
public interface IChannelMembershipService
{
	/// <summary>Adds a connection to a channel's members.</summary>
	/// <param name="channel">The channel to join.</param>
	/// <param name="by">The connection joining.</param>
	/// <returns>The outcome of the join.</returns>
	/// <remarks>
	///     A connection may join only a channel it may read; a connection acting for another user joins none. If the same user
	///     is a member through a closed connection of
	///     a kind that allows one connection, that connection is replaced by the new one, which
	///     <see cref="ChannelMemberJoined" /> reports.
	/// </remarks>
	ChannelJoinResult Join(ChannelSession channel, Connection by);

	/// <summary>Removes a connection from a channel's members.</summary>
	/// <param name="channel">The channel to part.</param>
	/// <param name="by">The connection parting.</param>
	/// <returns>The outcome of the part.</returns>
	ChannelPartResult Part(ChannelSession channel, Connection by);

	/// <summary>Joins a connection to every general channel joined automatically.</summary>
	/// <param name="by">The connection that just opened.</param>
	void JoinAutoChannels(Connection by);

	/// <summary>Removes a connection from every general channel.</summary>
	/// <param name="by">The connection that closed.</param>
	void PartAll(Connection by);
}