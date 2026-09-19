using Basil.Domain.Client;

namespace Basil.Domain.Chat;

/// <summary>
///     Represents a chat channel.
/// </summary>
public sealed class Channel : IEquatable<Channel>
{
	/// <summary>The unique identifier of the channel.</summary>
	public required int Id { get; init; }

	/// <summary>The channel name as used in chat.</summary>
	public required string Name { get; set; }

	/// <summary>The channel topic shown to joining users.</summary>
	public required string Topic { get; set; }

	/// <summary>The minimum privilege required to read the channel.</summary>
	public ClientPrivileges ReadPrivilege { get; set; } = ClientPrivileges.Player;

	/// <summary>The minimum privilege required to write to the channel.</summary>
	public ClientPrivileges WritePrivilege { get; set; } = ClientPrivileges.Player;

	/// <summary>A value that indicates whether the channel is joined automatically at login.</summary>
	public bool AutoJoin { get; set; } = false;

	/// <summary>Gets a value that indicates whether this channel equals another by id.</summary>
	/// <param name="other">The channel to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> is non-null and has the same
	///     <see cref="Id" /> as this channel; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(Channel? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>Gets a value that indicates whether this channel equals another object by id.</summary>
	/// <param name="obj">The object to compare, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Channel" /> that equals
	///     this one; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is Channel other && Equals(other);
	}

	/// <summary>Returns a hash code equal to the channel's <see cref="Id" />.</summary>
	/// <returns>A hash code consistent with the channel's value equality, which compares <see cref="Id" />.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}