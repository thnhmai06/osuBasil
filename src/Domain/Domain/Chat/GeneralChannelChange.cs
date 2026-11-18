using Basil.Domain.Game.Users;
using Basil.Shared.Utilities;

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
		if (ReadPrivileges is { } readPrivileges) readPrivileges.ThrowIfInvalid();
		if (WritePrivileges is { } writePrivileges) writePrivileges.ThrowIfInvalid();
		Topic?.ThrowIfHasNul();
	}

	public void ApplyTo(GeneralChannelData target)
	{
		if (Topic is { } topic) target.Topic = topic;
		if (ReadPrivileges is { } read) target.ReadPrivileges = read;
		if (WritePrivileges is { } write) target.WritePrivileges = write;
		if (AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
		if (Visible is { } visible) target.Visible = visible;
	}
}