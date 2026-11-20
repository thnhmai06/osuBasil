namespace Basil.Domain.Chat;

/// <summary>A rule of the chat module was broken: channels and messages.</summary>
public abstract class ChatException(string message) : BasilException(message);

/// <summary>A rule of a general channel was broken.</summary>
public abstract class ChannelException(string message) : ChatException(message);

/// <summary>A general channel name is not a valid channel name.</summary>
public sealed class InvalidChannelNameException() : ChannelException("Channel name is invalid.");

/// <summary>A general channel topic holds characters a topic cannot hold.</summary>
public sealed class InvalidChannelTopicException() : ChannelException("Text cannot contain the NUL character.");

/// <summary>A general channel read or write privilege is not a defined privilege combination.</summary>
public sealed class InvalidChannelPrivilegesException() : ChannelException("Privileges are not defined.");

/// <summary>A rule of the text of a chat message was broken.</summary>
public abstract class MessageException(string message) : ChatException(message);

/// <summary>A chat message has no text.</summary>
public sealed class EmptyMessageException() : MessageException("Message content cannot be empty.");

/// <summary>A chat message holds control characters.</summary>
public sealed class InvalidMessageException() : MessageException("Message content cannot hold control characters.");
