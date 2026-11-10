using Basil.Application.Common;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Persistence;

/// <summary>Stores what happened in matches.</summary>
public interface IMatchEventRepository
{
	/// <summary>Records an event of a match.</summary>
	/// <param name="matchEvent">The event to record.</param>
	Task RecordAsync(MatchEvent matchEvent);

	/// <summary>Lists the events of a match in the order they happened.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> events are returned, the earliest ones.</remarks>
	/// <param name="match">The match whose events to list.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The events of the match.</returns>
	Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match, CancellationToken ct = default);
}