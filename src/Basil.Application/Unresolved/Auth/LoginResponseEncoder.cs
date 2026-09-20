using Basil.Domain.Client;

namespace Basil.Application.Unresolved.Auth;

/// <summary>
///     Encodes the individual packets that make up a login response: the protocol handshake, the
///     failure replies, and the pieces of the welcome bundle a successful login assembles.
/// </summary>
/// <remarks>
///     The login flow decides what a login attempt found and in what order the bundle is
///     assembled; this type only turns each decided piece into its wire bytes, the same role
///     <see cref="PacketBuilders" /> plays for presence and stats.
/// </remarks>
public static class LoginResponseEncoder
{
	/// <summary>Builds the protocol-version packet every login response opens with.</summary>
	/// <returns>A byte array containing the wrapped protocol-version packet.</returns>
	public static byte[] ProtocolVersion()
	{
		return PacketWriter.ProtocolVersion(19);
	}

	/// <summary>Builds the login-reply packet reporting a new session's id.</summary>
	/// <param name="userId">The id of the session that was created.</param>
	/// <returns>A byte array containing the wrapped login-reply packet.</returns>
	public static byte[] SuccessReply(int userId)
	{
		return PacketWriter.LoginReply(userId);
	}

	/// <summary>Builds the login-reply packet reporting that authentication failed.</summary>
	/// <returns>A byte array containing the wrapped login-reply packet.</returns>
	public static byte[] AuthenticationFailedReply()
	{
		return PacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed);
	}

	/// <summary>Builds the login-reply packet reporting an unexpected server error.</summary>
	/// <returns>A byte array containing the wrapped login-reply packet.</returns>
	public static byte[] ServerErrorReply()
	{
		return PacketWriter.LoginReply((int)LoginFailureReason.ErrorOccurred);
	}

	/// <summary>Builds a notification packet carrying the given text.</summary>
	/// <param name="text">The notification text.</param>
	/// <returns>A byte array containing the wrapped notification packet.</returns>
	public static byte[] Notification(string text)
	{
		return PacketWriter.Notification(text);
	}

	/// <summary>Builds the bancho-privileges packet for a session's client-facing privilege set.</summary>
	/// <param name="privileges">The privilege set to report.</param>
	/// <returns>A byte array containing the wrapped bancho-privileges packet.</returns>
	public static byte[] BanchoPrivileges(ClientPrivileges privileges)
	{
		return PacketWriter.BanchoPrivileges((int)privileges);
	}

	/// <summary>Builds a channel-info packet describing a channel's name, topic and member count.</summary>
	/// <param name="name">The channel's name.</param>
	/// <param name="topic">The channel's topic.</param>
	/// <param name="playerCount">The channel's current member count.</param>
	/// <returns>A byte array containing the wrapped channel-info packet.</returns>
	public static byte[] ChannelInfo(string name, string topic, int playerCount)
	{
		return PacketWriter.ChannelInfo(name, topic, playerCount);
	}

	/// <summary>Builds the packet that marks the end of a login response's channel-info list.</summary>
	/// <returns>A byte array containing the wrapped channel-info-end packet.</returns>
	public static byte[] ChannelInfoEnd()
	{
		return PacketWriter.ChannelInfoEnd();
	}

	/// <summary>Builds the main-menu-icon packet pointing the client at an icon and its click target.</summary>
	/// <param name="iconUrl">The icon image's url, or empty for none.</param>
	/// <param name="onclickUrl">The url the icon opens when clicked.</param>
	/// <returns>A byte array containing the wrapped main-menu-icon packet.</returns>
	public static byte[] MainMenuIcon(string iconUrl, string onclickUrl)
	{
		return PacketWriter.MainMenuIcon(iconUrl, onclickUrl);
	}

	/// <summary>Builds the friends-list packet reporting a session's friend ids.</summary>
	/// <param name="friendIds">The ids of the session's friends.</param>
	/// <returns>A byte array containing the wrapped friends-list packet.</returns>
	public static byte[] FriendsList(IReadOnlyList<int> friendIds)
	{
		return PacketWriter.FriendsList(friendIds);
	}

	/// <summary>Builds the silence-end packet reporting how long a session's remaining silence lasts.</summary>
	/// <param name="remainingSeconds">The number of seconds left on the session's silence.</param>
	/// <returns>A byte array containing the wrapped silence-end packet.</returns>
	public static byte[] SilenceEnd(int remainingSeconds)
	{
		return PacketWriter.SilenceEnd(remainingSeconds);
	}

	/// <summary>Builds the packet telling a client its account is restricted.</summary>
	/// <returns>A byte array containing the wrapped account-restricted packet.</returns>
	public static byte[] AccountRestricted()
	{
		return PacketWriter.AccountRestricted();
	}
}