using Basil.Domain.Channels;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Chat;

/// <summary>
///     Tells connected players about channel membership and roster changes. The caller decides what
///     happened; the implementation decides how each player's transport carries it.
/// </summary>
public interface IChannelNotifier
{
	/// <summary>Confirms to a player that they joined a channel, including its current roster.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="self">The player who joined.</param>
	/// <param name="channel">The channel that was joined.</param>
	/// <param name="roster">The channel's current member names, prefixed by standing.</param>
	void Joined(UserSession self, ChannelSession channel, IReadOnlyList<string> roster);

	/// <summary>Confirms to a player that they left a channel.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="self">The player who left.</param>
	/// <param name="channel">The channel that was left.</param>
	/// <param name="kick">Whether the client should be told to drop the channel from its list.</param>
	void Left(UserSession self, ChannelSession channel, bool kick);

	/// <summary>Tells a channel's other members that one of them joined.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="channel">The channel that was joined.</param>
	/// <param name="member">The player who joined.</param>
	void MemberJoined(ChannelSession channel, UserSession member);

	/// <summary>Tells a channel's other members that one of them left.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="channel">The channel that was left.</param>
	/// <param name="member">The player who left.</param>
	void MemberLeft(ChannelSession channel, UserSession member);

	/// <summary>Tells the given members that a player has disconnected entirely.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="member">The player who disconnected.</param>
	/// <param name="tell">The ids of the members to notify.</param>
	/// <param name="reason">The reason reported for the disconnect.</param>
	void Quit(UserSession member, IReadOnlyCollection<int> tell, string reason);

	/// <summary>Tells a channel's members that its topic changed.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="channel">The channel whose topic changed.</param>
	/// <param name="by">The player or bot the change is attributed to.</param>
	/// <param name="topic">The new topic text.</param>
	void TopicChanged(ChannelSession channel, UserSession by, string topic);

	/// <summary>Tells a channel's readers that its roster or topic changed.</summary>
	/// <remarks>Never blocks on I/O: delivered from inside match-state transitions.</remarks>
	/// <param name="channel">The channel whose state changed.</param>
	void RosterChanged(ChannelSession channel);
}
