using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

/// <summary>The settings of a general channel to change; a <see langword="null" /> field stays as it is.</summary>
/// <param name="Topic">The new topic.</param>
/// <param name="ReadPrivileges">The roles a user must all have to read the channel.</param>
/// <param name="WritePrivileges">The roles a user must all have, besides the privilege to chat, to post in the channel.</param>
/// <param name="AutoJoin">Whether the channel is joined automatically at login.</param>
/// <param name="Visible">Whether the channel is shown in channel listings.</param>
public sealed record GeneralChannelChange(
	string? Topic = null,
	UserPrivileges? ReadPrivileges = null,
	UserPrivileges? WritePrivileges = null,
	bool? AutoJoin = null,
	bool? Visible = null)
{
	/// <summary>Gets a value that indicates whether every field that is set holds an acceptable value.</summary>
	public bool IsValid =>
		(Topic is null || GeneralChannelData.IsValidTopic(Topic)) &&
		(ReadPrivileges is not { } read || GeneralChannelData.IsValidPrivileges(read)) &&
		(WritePrivileges is not { } write || GeneralChannelData.IsValidPrivileges(write));
}