namespace Bancho.Application.Abstractions.Social;

/// <summary>
///     Ported from app/repositories/logs.py's LogsRepository, scoped to what ClientIntegrityService
///     needs: a single append-only insert. `fromId` is 0 (no real user has that id) for
///     system-detected flags with no admin actor, matching how Player.restrict/unrestrict use a real
///     admin's id for staff-initiated actions.
/// </summary>
public interface ILogRepository
{
    Task CreateAsync(int fromId, int toId, string action, string message,
        CancellationToken cancellationToken = default);
}