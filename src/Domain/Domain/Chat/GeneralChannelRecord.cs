using Basil.Domain.Game.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Chat;

public sealed class GeneralChannelRecord : IEquatable<GeneralChannelRecord>
{
	public required string Name
	{
		get;
		init
		{
			if (!IsValidName(value))
				throw new ArgumentException("Channel name is invalid.", nameof(value));
			field = value;
		}
	}

	public required GeneralChannelData Value { get; init; }

	public bool Equals(GeneralChannelRecord? other)
	{
		return other is not null && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name);
	}

	public static bool IsValidName(string? name)
	{
		return !string.IsNullOrEmpty(name) && name.Length >= 2 && name[0] == '#' && !name.Any(char.IsWhiteSpace) &&
		       !name.Contains(',') && !name.Contains('\0');
	}

	public override bool Equals(object? obj)
	{
		return obj is GeneralChannelRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
	}
}

public sealed class GeneralChannelData
{
	public string Topic
	{
		get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			if (!IsValidTopic(value))
				throw new ArgumentException("Text cannot contain the NUL character.", nameof(value));
			field = value;
		}
	} = string.Empty;

	public UserPrivileges ReadPrivileges
	{
		get;
		set => field = IsValidPrivileges(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
	} = UserPrivileges.None;

	public UserPrivileges WritePrivileges
	{
		get;
		set => field = IsValidPrivileges(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
	} = UserPrivileges.None;

	public bool AutoJoin { get; set; }

	public bool Visible { get; set; } = true;

	public static bool IsValidTopic(string? topic)
	{
		return topic is not null && !topic.HasNul();
	}

	public static bool IsValidPrivileges(UserPrivileges privileges)
	{
		return privileges.IsValid();
	}
}