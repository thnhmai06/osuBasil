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
			if (string.IsNullOrEmpty(value) || value is not ['#', _, ..] || value.Any(char.IsWhiteSpace) ||
			    value.Contains(',') || value.Contains('\0'))
				throw new ArgumentException("Channel name is invalid.", nameof(value));
			field = value;
		}
	}

	public required GeneralChannelData Value { get; init; }

	public bool Equals(GeneralChannelRecord? other)
	{
		return other is not null && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name);
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
			field = value.ThrowIfHasNul();
		}
	} = string.Empty;

	public UserPrivileges ReadPrivileges
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = UserPrivileges.None;

	public UserPrivileges WritePrivileges
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = UserPrivileges.None;

	public bool AutoJoin { get; set; }

	public bool Visible { get; set; } = true;
}