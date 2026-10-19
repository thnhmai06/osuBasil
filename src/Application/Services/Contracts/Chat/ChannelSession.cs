using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Common;
using Channel = Basil.Domain.Chat.Channel;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>A chat channel while it is open: its members and what happens in it.</summary>
/// <remarks>Opens a runtime channel for a chat channel.</remarks>
public abstract class ChannelSession
{
	/// <summary>The longest message kept; longer messages are cut.</summary>
	public const int MaxMessageLength = 2000;

	private readonly ConcurrentSet<Connection> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	/// <summary>Gets the chat channel this session runs.</summary>
	public abstract Channel Channel { get; }

	/// <summary>Gets the connections currently in the channel.</summary>
	public IReadOnlySet<Connection> Members => _members;

	/// <summary>
	///     Gets the connections currently in the channel, for changing them; the caller holds the scope and reports the
	///     change itself.
	/// </summary>
	internal ISet<Connection> MutableMembers => _members;

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
}
