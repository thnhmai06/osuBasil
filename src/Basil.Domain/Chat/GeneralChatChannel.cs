using Basil.Domain.Client;
using Basil.Domain.Utilities;

namespace Basil.Domain.Chat;

/// <summary>A configured chat channel open to every user with the required privileges.</summary>
public sealed class GeneralChatChannel(string name, string topic = "")
	: ChatChannel(name), IEquatable<GeneralChatChannel>
{
	/// <inheritdoc />
	public override string Topic { get; } = topic;

	/// <summary>Gets or sets the privileges a user must all hold to read the channel.</summary>
	public ClientPrivileges ReadPrivilege
	{
		get;
		set
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = ClientPrivileges.Player;

	/// <summary>Gets or sets the privileges a user must all hold to write to the channel.</summary>
	public ClientPrivileges WritePrivilege
	{
		get;
		set
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = ClientPrivileges.Player;

	/// <summary>A value that indicates whether the channel is joined automatically at login.</summary>
	public bool AutoJoin { get; set; } = false;

	/// <summary>A value that indicates whether the channel is visible in channel listings.</summary>
	public bool Visible { get; set; } = true;

	/// <summary>Determines whether another general channel has the same name.</summary>
	public bool Equals(GeneralChatChannel? other) => other is not null && Name == other.Name;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is GeneralChatChannel other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode() => Name.GetHashCode();
}