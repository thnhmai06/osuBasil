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

	public bool Equals(Channel? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is Channel other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}