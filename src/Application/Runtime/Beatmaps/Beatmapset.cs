using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Runtime.Common;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Runtime.Beatmaps;

/// <summary>Owns the state changes of one beatmapset.</summary>
public sealed class Beatmapset
{
	/// <summary>Creates the beatmapset object for a stored set.</summary>
	/// <param name="record">The set to represent.</param>
	/// <param name="context">The runtime context for beatmapset behaviour.</param>
	internal Beatmapset(BeatmapsetRecord record, BeatmapsetContext context)
	{
		Record = record;
		Context = context;
	}

	/// <summary>Gets the stored set this object represents.</summary>
	public BeatmapsetRecord Record { get; }

	/// <summary>The runtime context for beatmapset behaviour.</summary>
	internal BeatmapsetContext Context { get; }

	/// <summary>Scopes this beatmapset's behaviour to an actor and its authority.</summary>
	/// <param name="by">The actor acting on the beatmapset.</param>
	/// <returns>A short-lived scope for this beatmapset and actor.</returns>
	public ScopedBeatmapset ScopeTo(IActor by)
	{
		return new ScopedBeatmapset(this, by);
	}

	/// <summary>Forgets a stored beatmapset, leaving the files of its folder as they are.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal async Task ForgetAsync(CancellationToken cancellationToken)
	{
		await Context.Repository.CommitAsync(new BeatmapsetChanges([], [Record]), cancellationToken);
		Context.Events.Publish(new BeatmapsetDeleted(Record));
	}
}