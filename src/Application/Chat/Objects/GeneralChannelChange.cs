using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

public sealed record GeneralChannelChange(
	string? Topic = null,
	UserPrivileges? ReadPrivileges = null,
	UserPrivileges? WritePrivileges = null,
	bool? AutoJoin = null,
	bool? Visible = null)
{
	public bool IsValid =>
		(Topic is null || GeneralChannelData.IsValidTopic(Topic)) &&
		(ReadPrivileges is not { } read || GeneralChannelData.IsValidPrivileges(read)) &&
		(WritePrivileges is not { } write || GeneralChannelData.IsValidPrivileges(write));
}