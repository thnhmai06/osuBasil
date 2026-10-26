using Basil.Application.Persistence.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Persistence.Users;

/// <summary>Stores the moderation history of users.</summary>
/// <remarks>The log is append-only: entries are never changed or removed.</remarks>
public interface IModerationLogRepository
{
	/// <summary>Records a moderation action.</summary>
	/// <param name="entry">The action to record.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task AppendAsync(ModerationData entry, CancellationToken cancellationToken = default);

	/// <summary>Lists the entries recorded against a user, newest first.</summary>
	/// <param name="target">The user whose moderation history to list.</param>
	/// <param name="within">The moments the entries' timestamps must lie in, or <see langword="null" /> for all of them.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>One part of the user's moderation history, newest first, with how many entries it has in total.</returns>
	ValueTask<Page<ModerationLog>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken cancellationToken = default);
}