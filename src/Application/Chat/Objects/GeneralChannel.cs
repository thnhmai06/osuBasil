using Basil.Application.Chat.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannel : Channel
{
	/// <summary>The name of the general channel the multiplayer lobby uses for discussion.</summary>
	public const string LobbyName = "#lobby";

	internal GeneralChannel(GeneralChannelRecord record, ChannelContext context) : base(context)
	{
		Record = record;
	}

	/// <summary>Gets the stored identity and settings of this channel.</summary>
	public GeneralChannelRecord Record { get; }

	/// <inheritdoc />
	public override string Name => Record.Name;

	/// <inheritdoc />
	public override string Topic => Record.Value.Topic;

	/// <inheritdoc />
	internal override bool CanRead(Connection connection, UserPrivileges privileges)
	{
		return privileges.Allows(Record.Value.ReadPrivileges);
	}

	/// <inheritdoc />
	internal override bool CanWrite(Connection connection, UserPrivileges privileges)
	{
		return privileges.Allows(Record.Value.WritePrivileges);
	}

	/// <summary>Applies a change to the channel and removes the members who may no longer read it.</summary>
	/// <param name="change">The settings to change.</param>
	/// <param name="privilegesOf">Gives the effective privileges a member holds.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, or Closed when the channel has closed.</returns>
	internal async Task<ChannelOutcome> ConfigureAsync(GeneralChannelChange change,
		Func<Connection, UserPrivileges> privilegesOf, CancellationToken cancellationToken)
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

		await Context.Channels.CreateOrUpdateAsync(Record, cancellationToken);
		return ChannelOutcome.Ok;
	}

	/// <summary>Closes the channel and deletes it.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok.</returns>
	internal async Task<ChannelOutcome> DeleteAsync(CancellationToken cancellationToken)
	{
		Close();
		await Context.Channels.DeleteAsync(Record, cancellationToken);
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