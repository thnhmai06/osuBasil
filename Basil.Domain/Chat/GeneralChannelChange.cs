using Basil.Domain.Users;

namespace Basil.Domain.Chat;

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

	public void Validate()
	{
		if (Topic is not null && !GeneralChannelData.IsValidTopic(Topic)) throw new InvalidChannelTopicException();
		if ((ReadPrivileges is { } read && !GeneralChannelData.IsValidPrivileges(read)) ||
		    (WritePrivileges is { } write && !GeneralChannelData.IsValidPrivileges(write)))
			throw new InvalidChannelPrivilegesException();
	}

	public void ApplyTo(GeneralChannelData target)
	{
		if (Topic != null) target.Topic = Topic;
		if (ReadPrivileges is { } read) target.ReadPrivileges = read;
		if (WritePrivileges is { } write) target.WritePrivileges = write;
		if (AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
		if (Visible is { } visible) target.Visible = visible;
	}
}