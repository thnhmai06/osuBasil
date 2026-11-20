using Basil.Domain.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Chat;

/// <summary>A stored general channel: its name and its settings.</summary>
/// <remarks>Two records are equal when their names are equal, ignoring case.</remarks>
public sealed class GeneralChannelRecord : IEquatable<GeneralChannelRecord>
{
	/// <summary>The name of the channel.</summary>
	/// <exception cref="InvalidChannelNameException">The value is not a valid channel name.</exception>
	public required string Name
	{
		get;
		init
		{
			if (!IsValidName(value))
				throw new InvalidChannelNameException();
			field = value;
		}
	}

	/// <summary>The settings of the channel.</summary>
	public required GeneralChannelData Value { get; init; }

	public bool Equals(GeneralChannelRecord? other)
	{
		return other is not null && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name);
	}

	/// <summary>Checks whether a text is a valid channel name: at least two characters, starting with <c>#</c>, with no whitespace, comma or NUL character.</summary>
	/// <param name="name">The text to check.</param>
	/// <returns><see langword="true" /> when the name is valid.</returns>
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

/// <summary>The settings of a general channel.</summary>
public sealed class GeneralChannelData
{
	/// <summary>The topic of the channel; empty by default.</summary>
	/// <exception cref="InvalidChannelTopicException">The value holds a NUL character.</exception>
	public string Topic
	{
		get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			if (!IsValidTopic(value))
				throw new InvalidChannelTopicException();
			field = value;
		}
	} = string.Empty;

	/// <summary>The privileges a user needs to read the channel; <see cref="UserPrivileges.None" /> by default, which lets everyone read.</summary>
	/// <exception cref="InvalidChannelPrivilegesException">The value is not a defined combination.</exception>
	public UserPrivileges ReadPrivileges
	{
		get;
		set => field = IsValidPrivileges(value)
			? value
			: throw new InvalidChannelPrivilegesException();
	} = UserPrivileges.None;

	/// <summary>The privileges a user needs to write to the channel; <see cref="UserPrivileges.None" /> by default, which lets everyone write.</summary>
	/// <exception cref="InvalidChannelPrivilegesException">The value is not a defined combination.</exception>
	public UserPrivileges WritePrivileges
	{
		get;
		set => field = IsValidPrivileges(value)
			? value
			: throw new InvalidChannelPrivilegesException();
	} = UserPrivileges.None;

	/// <summary>Whether users join the channel automatically when they sign in.</summary>
	public bool AutoJoin { get; set; }

	/// <summary>Whether the channel is listed; it is by default.</summary>
	public bool Visible { get; set; } = true;

	/// <summary>Checks whether a text can be a topic: it is not <see langword="null" /> and holds no NUL character.</summary>
	/// <param name="topic">The text to check.</param>
	/// <returns><see langword="true" /> when the topic is valid.</returns>
	public static bool IsValidTopic(string? topic)
	{
		return topic is not null && !topic.HasNul();
	}

	/// <summary>Checks whether a value is a defined combination of privileges.</summary>
	/// <param name="privileges">The value to check.</param>
	/// <returns><see langword="true" /> when the value is valid.</returns>
	public static bool IsValidPrivileges(UserPrivileges privileges)
	{
		return privileges.IsValid();
	}
}