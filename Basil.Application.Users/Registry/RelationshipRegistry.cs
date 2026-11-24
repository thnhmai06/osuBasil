using System.Collections.Concurrent;
using System.Collections.Frozen;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Application.Users.Registry;

/// <summary>Keeps in memory, per user, the ids of the users that user has each kind of relationship with, and applies every change to the store and to that memory together.</summary>
/// <remarks>A user is loaded once, the first time something asks about them; the synchronous queries answer only for a loaded user and see no one otherwise.</remarks>
public sealed class RelationshipRegistry
{
	private static readonly RelationshipType[] Types = Enum.GetValues<RelationshipType>();

	private readonly TimeProvider _clock;
	private readonly ConcurrentDictionary<int, Lazy<Task<FrozenDictionary<RelationshipType, ConcurrentSet<int>>>>> _held = new();
	private readonly IRelationshipRepository _repository;

	internal RelationshipRegistry(IRelationshipRepository repository, TimeProvider clock)
	{
		_repository = repository;
		_clock = clock;
	}

	/// <summary>Loads the relationships of <paramref name="actor"/> if they are not loaded yet.</summary>
	public async Task EnsureLoadedAsync(UserRecord actor, CancellationToken ct = default)
	{
		await HeldAsync(actor, ct);
	}

	/// <summary>Gets whether <paramref name="actorId"/> has a <paramref name="type"/> relationship with <paramref name="targetId"/>; false while the actor is not loaded.</summary>
	public bool Has(int actorId, RelationshipType type, int targetId)
	{
		return Loaded(actorId)?[type].Contains(targetId) ?? false;
	}

	/// <summary>Gets the ids <paramref name="actorId"/> has a <paramref name="type"/> relationship with; empty while the actor is not loaded.</summary>
	public IReadOnlySet<int> Of(int actorId, RelationshipType type)
	{
		return Loaded(actorId) is { } held ? held[type] : FrozenSet<int>.Empty;
	}

	/// <summary>Gets whether <paramref name="actor"/> has a <paramref name="type"/> relationship with <paramref name="targetId"/>, loading the actor first.</summary>
	public async Task<bool> HasAsync(UserRecord actor, RelationshipType type, int targetId,
		CancellationToken ct = default)
	{
		return (await HeldAsync(actor, ct))[type].Contains(targetId);
	}

	/// <summary>Gets the ids <paramref name="actor"/> has a <paramref name="type"/> relationship with, loading the actor first.</summary>
	public async Task<IReadOnlySet<int>> OfAsync(UserRecord actor, RelationshipType type,
		CancellationToken ct = default)
	{
		return (await HeldAsync(actor, ct))[type];
	}

	/// <summary>Sets a <paramref name="type"/> relationship from <paramref name="actor"/> to <paramref name="target"/>, replacing any other kind between them.</summary>
	public async Task SetAsync(UserRecord actor, UserRecord target, RelationshipType type)
	{
		var held = await HeldAsync(actor, CancellationToken.None);
		await _repository.CreateOrUpdateAsync(NewRelationship(actor, target, type));

		held[type].Add(target.Id);
		foreach (var other in Types)
			if (other != type)
				held[other].Remove(target.Id);
	}

	/// <summary>Removes the <paramref name="type"/> relationship from <paramref name="actor"/> to <paramref name="target"/>; false when there was none.</summary>
	public async Task<bool> RemoveAsync(UserRecord actor, UserRecord target, RelationshipType type)
	{
		var held = await HeldAsync(actor, CancellationToken.None);
		if (!held[type].Contains(target.Id)) return false;

		await _repository.DeleteAsync(NewRelationship(actor, target, type));
		held[type].Remove(target.Id);
		return true;
	}

	private Relationship NewRelationship(UserRecord actor, UserRecord target, RelationshipType type)
	{
		return new Relationship { Actor = actor, Target = target, Type = type, CreatedAt = _clock.GetUtcNow() };
	}

	private FrozenDictionary<RelationshipType, ConcurrentSet<int>>? Loaded(int actorId)
	{
		return _held.TryGetValue(actorId, out var entry) && entry is { IsValueCreated: true, Value.IsCompletedSuccessfully: true }
			? entry.Value.Result
			: null;
	}

	private async Task<FrozenDictionary<RelationshipType, ConcurrentSet<int>>> HeldAsync(UserRecord actor,
		CancellationToken ct)
	{
		// One lazy load per user, so readers that arrive together share it; the load is not cut short by one caller leaving.
		var entry = _held.GetOrAdd(actor.Id,
			_ => new Lazy<Task<FrozenDictionary<RelationshipType, ConcurrentSet<int>>>>(() => ReadAsync(actor)));
		try
		{
			return await entry.Value.WaitAsync(ct);
		}
		catch when (entry.Value.IsFaulted)
		{
			// A failed load is not kept: the next ask loads again.
			_held.TryRemove(new KeyValuePair<int, Lazy<Task<FrozenDictionary<RelationshipType, ConcurrentSet<int>>>>>(
				actor.Id, entry));
			throw;
		}
	}

	private async Task<FrozenDictionary<RelationshipType, ConcurrentSet<int>>> ReadAsync(UserRecord actor)
	{
		var stored = await _repository.ListAsync(actor);
		var held = Types.ToDictionary(type => type, _ => new ConcurrentSet<int>());
		foreach (var relationship in stored)
			held[relationship.Type].Add(relationship.Target.Id);

		return held.ToFrozenDictionary();
	}
}
