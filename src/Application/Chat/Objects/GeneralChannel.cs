using Basil.Application.Chat.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Chat;

using Basil.Domain.Users;
using Basil.Domain.Game.Users;

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

	internal override bool CanRead(Session connection, UserPrivileges privileges)
	{
		return privileges.Allows(Record.Value.ReadPrivileges);
	}

	internal override bool CanWrite(Session connection, UserPrivileges privileges)
	{
		return privileges.Allows(Record.Value.WritePrivileges);
	}

	internal async Task<ChannelOutcome> ConfigureAsync(GeneralChannelChange change,
		Func<Session, UserPrivileges> privilegesOf)
	{
		using (Enter())
		{
			if (IsClosed) return ChannelOutcome.Closed;
			var target = Record.Value;

			change.Validate();
			change.ApplyTo(target);

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
}