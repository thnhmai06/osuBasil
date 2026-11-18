using Basil.Application.Chat.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

public sealed class GeneralChannel : Channel
{
	public const string LobbyName = "#lobby";

	internal GeneralChannel(GeneralChannelRecord record, ChannelContext context) : base(context)
	{
		Record = record;
	}

	public GeneralChannelRecord Record { get; }

	public override string Name => Record.Name;

	public override string Topic => Record.Value.Topic;

	internal override bool CanRead(Connection connection, UserPrivileges privileges)
	{
		return privileges.Allows(Record.Value.ReadPrivileges);
	}

	internal override bool CanWrite(Connection connection, UserPrivileges privileges)
	{
		return privileges.Allows(Record.Value.WritePrivileges);
	}

	internal async Task<ChannelOutcome> ConfigureAsync(GeneralChannelChange change,
		Func<Connection, UserPrivileges> privilegesOf)
	{
		using (Enter())
		{
			if (IsClosed) return ChannelOutcome.Closed;
			var target = Record.Value;

			Validate(change);
			Apply(target, change);

			var removed = Members.Where(member => !CanRead(member, privilegesOf(member))).ToArray();
			foreach (var member in removed)
				MutableMembers.Remove(member);
			Context.Events.Publish(new ChannelConfigured(this, change, removed));
		}

		await Context.Channels.CreateOrUpdateAsync(Record);
		return ChannelOutcome.Ok;
	}

	internal async Task<ChannelOutcome> DeleteAsync()
	{
		Close();
		await Context.Channels.DeleteAsync(Record);
		return ChannelOutcome.Ok;
	}

	private static void Validate(GeneralChannelChange change)
	{
		if (change.ReadPrivileges is { } readPrivileges) readPrivileges.ThrowIfUndefined();
		if (change.WritePrivileges is { } writePrivileges) writePrivileges.ThrowIfUndefined();
		change.Topic?.ThrowIfHasNul();
	}

	private static void Apply(GeneralChannelData target, GeneralChannelChange change)
	{
		if (change.Topic is { } topic) target.Topic = topic;
		if (change.ReadPrivileges is { } read) target.ReadPrivileges = read;
		if (change.WritePrivileges is { } write) target.WritePrivileges = write;
		if (change.AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
		if (change.Visible is { } visible) target.Visible = visible;
	}
}