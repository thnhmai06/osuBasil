using Basil.Domain.Client;

namespace Basil.Domain.Chat;

/// <summary>
///     Represents a chat channel.
/// </summary>
public sealed class ChatChannel : IChannel
{
	public required string Name
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Channel name cannot be empty.", nameof(value))
			: value;
	}

	/// <summary>The channel topic shown to joining users.</summary>
	public string Topic { get; set; } = string.Empty;

	public string DisplayName => Name;

	/// <summary>The minimum privilege required to read the channel.</summary>
	public ClientPrivileges ReadPrivilege
	{
		get;
		set => field = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "ClientPrivileges is not a defined value.");
	} = ClientPrivileges.Player;

	/// <summary>The minimum privilege required to write to the channel.</summary>
	public ClientPrivileges WritePrivilege
	{
		get;
		set => field = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "ClientPrivileges is not a defined value.");
	} = ClientPrivileges.Player;

	/// <summary>A value that indicates whether the channel is joined automatically at login.</summary>
	public bool AutoJoin { get; set; } = false;

	public bool Visible { get; set; } = true;

	public bool Equals(IChannel? other)
	{
		if (other is null) return false;
		return Name == other.Name;
	}

	public override bool Equals(object? obj)
	{
		return obj is IChannel other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Name.GetHashCode();
	}
}