namespace Basil.Application.Abstractions.Social;

/// <summary>
///     Records administrative actions against users in the UserLogs table.
/// </summary>
/// <remarks>
///     Scoped to what client integrity checking needs: a single append-only insert. The acting user
///     id is 0 for system-detected flags that have no admin actor, since no real user has that id;
///     staff-initiated actions pass the acting admin's real id instead.
/// </remarks>
public interface ILogRepository
{
	/// <summary>
	///     Appends a log entry recording an action one user took toward another.
	/// </summary>
	/// <param name="fromId">The id of the user who performed the action, or 0 for the system.</param>
	/// <param name="toId">The id of the user the action targeted.</param>
	/// <param name="action">A short action name, for example a privilege change or a flag.</param>
	/// <param name="message">A human-readable description of the action.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateAsync(int fromId, int toId, string action, string message,
		CancellationToken cancellationToken = default);
}