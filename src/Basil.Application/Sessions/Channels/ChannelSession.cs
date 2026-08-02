using System.Collections.Concurrent;
using Basil.Domain.Users;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Represents a single chat channel in memory: the immutable DB-backed metadata plus the live
///     set of member ids. <see cref="Name" /> is the registry key; <see cref="DisplayName" /> is
///     what is actually sent to clients in packets. For ordinary channels the two are the same, but
///     an instance channel (for example <c>#spec_123</c>) always displays as a fixed alias
///     (<c>#spectator</c>) regardless of which instance a given client is currently in, because
///     multiple instances exist concurrently server-side.
/// </summary>
/// <param name="id">The persistent id of the channel's database row, or a runtime-assigned value for an instance channel.</param>
/// <param name="name">The registry key of the channel.</param>
/// <param name="topic">The channel's topic text shown to its members.</param>
/// <param name="readPrivilege">The privilege flags required to read the channel, or zero for no requirement.</param>
/// <param name="writePrivilege">The privilege flags required to write to the channel, or zero for no requirement.</param>
/// <param name="autoJoin">A value that indicates whether the channel is joined automatically at login.</param>
/// <param name="displayName">The name sent to clients, defaulting to <paramref name="name" /> when omitted.</param>
/// <param name="instance">
///     A value that indicates whether this is a runtime-created instance channel rather than a
///     DB-backed one.
/// </param>
public sealed class ChannelSession(
	int id,
	string name,
	string topic,
	UserPrivileges readPrivilege,
	UserPrivileges writePrivilege,
	bool autoJoin,
	string? displayName = null,
	bool instance = false)
{
	private readonly ConcurrentDictionary<int, byte> _members = new();

	/// <summary>Gets the channel's id.</summary>
	public int Id { get; } = id;

	/// <summary>Gets the registry key of the channel, used to look it up and to key a userSession's membership.</summary>
	public string Name { get; } = name;

	/// <summary>
	///     Gets the name sent to clients in packets, equal to <see cref="Name" /> for ordinary channels and a fixed alias
	///     for instance channels.
	/// </summary>
	public string DisplayName { get; } = displayName ?? name;

	/// <summary>Gets the channel's topic text.</summary>
	public string Topic { get; } = topic;

	/// <summary>Gets the privilege flags required to read the channel, or zero when reading is unrestricted.</summary>
	public UserPrivileges ReadPrivilege { get; } = readPrivilege;

	/// <summary>Gets the privilege flags required to write to the channel, or zero when writing is unrestricted.</summary>
	public UserPrivileges WritePrivilege { get; } = writePrivilege;

	/// <summary>
	///     Gets a value that indicates whether the channel is joined automatically for every qualifying userSession at
	///     login.
	/// </summary>
	public bool AutoJoin { get; } = autoJoin;

	/// <summary>
	///     Gets a value that indicates whether this is a runtime-created instance channel, such as a spectator channel,
	///     rather than a DB-backed one.
	/// </summary>
	public bool Instance { get; } = instance;

	/// <summary>Gets the number of players currently in the channel.</summary>
	public int PlayerCount => _members.Count;

	/// <summary>Gets the ids of the players currently in the channel, as a snapshot collection.</summary>
	public IReadOnlyCollection<int> MemberIds => [.. _members.Keys];

	/// <summary>
	///     Gets a value that indicates whether a userSession with <paramref name="privilege" /> may read
	///     this channel: no read requirement, or at least one required flag present.
	/// </summary>
	/// <param name="privilege">The privilege flags of the userSession to check.</param>
	/// <returns><see langword="true" /> if the userSession may read the channel; otherwise, <see langword="false" />.</returns>
	public bool CanRead(UserPrivileges privilege)
	{
		return ReadPrivilege == 0 || (privilege & ReadPrivilege) != 0;
	}

	/// <summary>
	///     Gets a value that indicates whether a userSession with <paramref name="privilege" /> may write
	///     to this channel: no write requirement, or at least one required flag present.
	/// </summary>
	/// <param name="privilege">The privilege flags of the userSession to check.</param>
	/// <returns><see langword="true" /> if the userSession may write to the channel; otherwise, <see langword="false" />.</returns>
	public bool CanWrite(UserPrivileges privilege)
	{
		return WritePrivilege == 0 || (privilege & WritePrivilege) != 0;
	}

	/// <summary>
	///     Adds a userSession to the channel's member set.
	/// </summary>
	/// <param name="playerId">The id of the userSession joining.</param>
	public void Join(int playerId)
	{
		_members[playerId] = 0;
	}

	/// <summary>
	///     Removes a userSession from the channel's member set.
	/// </summary>
	/// <param name="playerId">The id of the userSession leaving.</param>
	public void Part(int playerId)
	{
		_members.TryRemove(playerId, out _);
	}

	/// <summary>
	///     Gets a value that indicates whether a userSession is currently in the channel.
	/// </summary>
	/// <param name="playerId">The id of the userSession to check.</param>
	/// <returns><see langword="true" /> if the userSession is a member; otherwise, <see langword="false" />.</returns>
	public bool Contains(int playerId)
	{
		return _members.ContainsKey(playerId);
	}
}