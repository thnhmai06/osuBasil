using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>A configured chat channel open to every user with the required roles.</summary>
public sealed class GeneralChannel : Channel, IEquatable<GeneralChannel>
{
	/// <summary>Gets or sets the roles a user must all have to read the channel; <see cref="Privileges.None" /> lets anyone read.</summary>
	public Privileges ReadPrivileges
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = Privileges.None;

	/// <summary>Gets or sets the roles a user must all have, besides the right to chat, to post in the channel; <see cref="Privileges.None" /> lets anyone post.</summary>
	public Privileges WritePrivileges
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = Privileges.None;

	/// <summary>A value that indicates whether the channel is joined automatically at login.</summary>
	public bool AutoJoin { get; set; } = false;

	/// <summary>A value that indicates whether the channel is visible in channel listings.</summary>
	public bool Visible { get; set; } = true;

	/// <summary>Determines whether another general channel has the same name.</summary>
	public bool Equals(GeneralChannel? other)
	{
		return other is not null && Name == other.Name;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GeneralChannel other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Name.GetHashCode();
	}
}