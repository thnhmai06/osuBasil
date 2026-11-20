using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>A partial update of a general channel: every value that is set replaces the channel's current one, and every value left <see langword="null" /> keeps it.</summary>
/// <param name="Topic">The new topic.</param>
/// <param name="ReadPrivileges">The new privileges needed to read the channel.</param>
/// <param name="WritePrivileges">The new privileges needed to write to the channel.</param>
/// <param name="AutoJoin">Whether users join the channel automatically.</param>
/// <param name="Visible">Whether the channel is listed.</param>
public sealed record GeneralChannelChange(
	string? Topic = null,
	UserPrivileges? ReadPrivileges = null,
	UserPrivileges? WritePrivileges = null,
	bool? AutoJoin = null,
	bool? Visible = null)
{
	/// <summary>Whether every value that is set would be accepted by the channel.</summary>
	public bool IsValid =>
		(Topic is null || GeneralChannelData.IsValidTopic(Topic)) &&
		(ReadPrivileges is not { } read || GeneralChannelData.IsValidPrivileges(read)) &&
		(WritePrivileges is not { } write || GeneralChannelData.IsValidPrivileges(write));

	/// <summary>Checks every value that is set.</summary>
	/// <exception cref="InvalidChannelTopicException">The topic holds a NUL character.</exception>
	/// <exception cref="InvalidChannelPrivilegesException">A privilege value is not a defined combination.</exception>
	public void Validate()
	{
		if (Topic is not null && !GeneralChannelData.IsValidTopic(Topic)) throw new InvalidChannelTopicException();
		if ((ReadPrivileges is { } read && !GeneralChannelData.IsValidPrivileges(read)) ||
		    (WritePrivileges is { } write && !GeneralChannelData.IsValidPrivileges(write)))
			throw new InvalidChannelPrivilegesException();
	}

	/// <summary>Applies every value that is set to a channel and leaves the rest as it is.</summary>
	/// <param name="target">The channel data to change.</param>
	public void ApplyTo(GeneralChannelData target)
	{
		if (Topic != null) target.Topic = Topic;
		if (ReadPrivileges is { } read) target.ReadPrivileges = read;
		if (WritePrivileges is { } write) target.WritePrivileges = write;
		if (AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
		if (Visible is { } visible) target.Visible = visible;
	}
}