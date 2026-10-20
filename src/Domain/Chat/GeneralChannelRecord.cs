using Basil.Domain.Common;
using Basil.Domain.Permissions;

namespace Basil.Domain.Chat;

/// <summary>A configured chat channel identified by its name.</summary>
public sealed class GeneralChannelRecord : IEquatable<GeneralChannelRecord>
{
	/// <summary>Gets the channel name that identifies this record.</summary>
	/// <exception cref="ArgumentException">The name does not follow the channel naming rules.</exception>
	public required string Name
	{
		get;
		init
		{
			if (string.IsNullOrEmpty(value) || value.Length < 2 || value[0] != '#' || value.Any(char.IsWhiteSpace) ||
			    value.Contains(',') || value.Contains('\0'))
				throw new ArgumentException("Channel name is invalid.", nameof(value));
			field = value;
		}
	}

	/// <summary>Gets the settings stored for this channel.</summary>
	public required GeneralChannelData Value { get; init; }

	/// <summary>Determines whether another record identifies the same channel.</summary>
	/// <param name="other">The record to compare against, or <see langword="null" />.</param>
	/// <returns><see langword="true" /> when both records have the same name, ignoring case.</returns>
	public bool Equals(GeneralChannelRecord? other)
	{
		return other is not null && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GeneralChannelRecord other && Equals(other);
	}

	/// <summary>Returns the hash code of this channel identity.</summary>
	/// <returns>The hash code of <see cref="Name" />, ignoring case.</returns>
	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
	}
}

/// <summary>The settings of a configured chat channel.</summary>
public sealed class GeneralChannelData
{
	/// <summary>Gets or sets the topic shown to users who join the channel.</summary>
	public string Topic
	{
		get;
		set => field = value.ThrowIfHasNul();
	} = string.Empty;

	/// <summary>Gets or sets the roles a user must all have to read the channel; <see cref="Privileges.None" /> lets anyone read.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value sets an undefined privilege bit.</exception>
	public Privileges ReadPrivileges
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = Privileges.None;

	/// <summary>Gets or sets the roles a user must all have, besides the right to chat, to post in the channel; <see cref="Privileges.None" /> lets anyone post.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value sets an undefined privilege bit.</exception>
	public Privileges WritePrivileges
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = Privileges.None;

	/// <summary>A value that indicates whether the channel is joined automatically at login.</summary>
	public bool AutoJoin { get; set; }

	/// <summary>A value that indicates whether the channel is visible in channel listings.</summary>
	public bool Visible { get; set; } = true;
}