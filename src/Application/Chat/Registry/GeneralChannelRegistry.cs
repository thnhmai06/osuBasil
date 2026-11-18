using System.Collections.Concurrent;
using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Chat.Registry;

public sealed class GeneralChannelRegistry
{
	private readonly ConcurrentDictionary<string, GeneralChannel> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyDictionary<string, GeneralChannel> All => _channels;

	internal bool Add(GeneralChannel channel)
	{
		return _channels.TryAdd(channel.Name, channel);
	}

	internal bool Remove(GeneralChannel channel)
	{
		return _channels.TryRemove(new KeyValuePair<string, GeneralChannel>(channel.Name, channel));
	}

	public GeneralChannel? Find(string name)
	{
		return _channels.GetValueOrDefault(name);
	}

	public IEnumerable<GeneralChannel> VisibleTo(Connection actor)
	{
		foreach (var (_, channel) in _channels)
		{
			var privileges = actor.EffectivePrivileges(channel.Context.Clock.GetUtcNow());
			if (!channel.IsClosed && channel.CanRead(actor, privileges) &&
			    (channel.Record.Value.Visible || privileges.Allows(UserPrivileges.Moderator)))
				yield return channel;
		}
	}

	public void JoinAutoChannels(Connection actor)
	{
		foreach (var channel in All.Values.Where(channel => channel.Record.Value.AutoJoin))
			channel.ScopeTo(actor).Join();
	}

	public void PartAll(Connection actor)
	{
		foreach (var channel in All.Values)
			channel.ScopeTo(actor).Part();
	}
}