using System.Collections.Concurrent;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Sessions.Channels;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IChannelRegistry" />
/// <remarks>
///     Backed by a <see cref="ConcurrentDictionary{TKey,TValue}" /> keyed by channel name, making
///     every lookup and mutation thread-safe. Seeding copies the immutable DB metadata from each
///     <see cref="Channel" /> into a live <see cref="ChannelSession" /> that then carries its own
///     membership state.
/// </remarks>
public sealed class InMemoryChannelRegistry : IChannelRegistry
{
	private readonly ConcurrentDictionary<string, ChannelSession> _byName = new();

	/// <inheritdoc />
	/// <remarks>
	///     Assigns each channel by name. Names not present in <paramref name="channels" /> are left
	///     unchanged, so the full set of DB-backed channels must be passed to fully reset the
	///     registry.
	/// </remarks>
	public void Seed(IReadOnlyList<Channel> channels)
	{
		foreach (var channel in channels)
			_byName[channel.Name] = new ChannelSession(
				channel.Id, channel.Name, channel.Topic, channel.ReadPrivilege, channel.WritePrivilege,
				channel.AutoJoin);
	}

	/// <inheritdoc />
	/// <remarks>Overwrites any existing channel registered under the same name.</remarks>
	public void Add(ChannelSession channel)
	{
		_byName[channel.Name] = channel;
	}

	/// <inheritdoc />
	public void Remove(string name)
	{
		_byName.TryRemove(name, out _);
	}

	/// <inheritdoc />
	public ChannelSession? GetByName(string name)
	{
		return _byName.GetValueOrDefault(name);
	}

	/// <inheritdoc />
	public IReadOnlyList<ChannelSession> AutoJoinChannels =>
		[.. _byName.Values.Where(c => c.AutoJoin)];

	/// <inheritdoc />
	public IReadOnlyList<ChannelSession> All => [.. _byName.Values];
}