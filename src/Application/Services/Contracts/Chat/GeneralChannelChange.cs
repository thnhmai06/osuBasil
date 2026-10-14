using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>The settings of a general channel to change; a <see langword="null" /> field stays as it is.</summary>
/// <param name="Topic">The new topic.</param>
/// <param name="ReadPermissions">The permissions a user must all hold to read the channel.</param>
/// <param name="WritePermissions">The permissions a user must all hold, besides the right to chat, to post in the channel.</param>
/// <param name="AutoJoin">Whether the channel is joined automatically at login.</param>
/// <param name="Visible">Whether the channel is shown in channel listings.</param>
public sealed record GeneralChannelChange(
	string? Topic = null,
	Permissions? ReadPermissions = null,
	Permissions? WritePermissions = null,
	bool? AutoJoin = null,
	bool? Visible = null);