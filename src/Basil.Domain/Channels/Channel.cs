using Basil.Domain.Users;

namespace Basil.Domain.Channels;

/// <summary>
///     Represents a chat channel.
/// </summary>
/// <param name="Id">The unique identifier of the channel.</param>
/// <param name="Name">The channel name as used in chat.</param>
/// <param name="Topic">The channel topic shown to joining users.</param>
/// <param name="ReadPrivilege">The minimum privilege required to read the channel.</param>
/// <param name="WritePrivilege">The minimum privilege required to write to the channel.</param>
/// <param name="AutoJoin">
///     A value that indicates whether the channel is joined automatically at login.
/// </param>
public sealed record Channel(
	int Id,
	string Name,
	string Topic,
	UserPrivileges ReadPrivilege,
	UserPrivileges WritePrivilege,
	bool AutoJoin);