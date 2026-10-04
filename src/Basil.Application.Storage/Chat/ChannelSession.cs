using Basil.Application.Storage.Sessions;
using Basil.Domain.Utilities;
using Channel = Basil.Domain.Chat.Channel;

namespace Basil.Application.Storage.Chat;

/// <summary>A chat channel while it is open: its members and what happens in it.</summary>
/// <remarks>Opens a runtime channel for a chat channel.</remarks>
/// <param name="channel">The chat channel this session runs.</param>
public abstract class ChannelSession(Channel channel)
{
	/// <summary>The longest message kept; longer messages are cut.</summary>
	public const int MaxMessageLength = 2000;

	private readonly ConcurrentSet<Connection> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	/// <summary>Gets the chat channel this session runs.</summary>
	public Channel Channel { get; } = channel;

	/// <summary>
	///     Gets the channel name: <c>#name</c> for a channel several users take part in, the owner's name for a
	///     private-message channel.
	/// </summary>
	public string Name => Channel.Name;

	/// <summary>Gets the connections currently in the channel.</summary>
	public IReadOnlySet<Connection> Members => _members;

	/// <summary>Gets a value that indicates whether the channel has been closed.</summary>
	public bool IsClosed
	{
		get => _closed;
		internal set => _closed = value;
	}

	/// <summary>
	///     Enters the scope in which the channel's members change one at a time.
	/// </summary>
	/// <returns>A scope that must be disposed when the membership change is complete.</returns>
	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	/// <summary>Adds a member without emitting an event; the caller holds the scope and reports the change itself.</summary>
	/// <returns><see langword="true" /> if the connection was not already a member.</returns>
	internal bool AddMember(Connection connection)
	{
		return _members.Add(connection);
	}

	/// <summary>
	///     Removes a member without emitting an event; the caller holds the scope and reports the change
	///     itself.
	/// </summary>
	/// <returns><see langword="true" /> if the connection was a member.</returns>
	internal bool RemoveMember(Connection connection)
	{
		return _members.Remove(connection);
	}
}