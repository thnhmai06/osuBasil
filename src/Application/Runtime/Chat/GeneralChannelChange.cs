using Basil.Domain.Permissions;

namespace Basil.Application.Runtime.Chat;

/// <summary>The settings of a general channel to change; a <see langword="null" /> field stays as it is.</summary>
/// <param name="Topic">The new topic.</param>
/// <param name="ReadPrivileges">The roles a user must all have to read the channel.</param>
/// <param name="WritePrivileges">The roles a user must all have, besides the right to chat, to post in the channel.</param>
/// <param name="AutoJoin">Whether the channel is joined automatically at login.</param>
/// <param name="Visible">Whether the channel is shown in channel listings.</param>
public sealed record GeneralChannelChange(
	string? Topic = null,
	Privileges? ReadPrivileges = null,
	Privileges? WritePrivileges = null,
	bool? AutoJoin = null,
	bool? Visible = null);