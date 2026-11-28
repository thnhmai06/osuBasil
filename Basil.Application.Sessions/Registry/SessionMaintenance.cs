using Basil.Application.Sessions.Objects;
using Basil.Domain.Sessions;

namespace Basil.Application.Sessions.Registry;

/// <summary>Keeps the stored sessions tidy: writes their activity, ends the silent and stopped ones and purges old ones.</summary>
public sealed class SessionMaintenance
{
	private readonly ActivityLog _activity;
	private readonly SessionContext _context;

	internal SessionMaintenance(SessionContext context, ActivityLog activity)
	{
		_context = context;
		_activity = activity;
	}

	/// <summary>Writes the last-seen time of the sessions heard from since the last write.</summary>
	public async Task FlushActivityAsync()
	{
		await _context.Sessions.WriteActivityAsync(_activity.Take());
	}

	/// <summary>Ends the stored sessions that went silent past their expiry.</summary>
	public Task<int> EndSilentAsync()
	{
		return _context.Sessions.EndSilentAsync(_context.Clock.GetUtcNow());
	}

	/// <summary>Ends the stored sessions a stopped server left open, except the ones that resume by token.</summary>
	public async Task<int> CloseStoppedAsync()
	{
		// Only an API session resumes by its token; osu! and IRC clients sign in again after a restart.
		var now = _context.Clock.GetUtcNow();
		var ended = 0;
		foreach (var type in new[] { SessionType.Stable, SessionType.StableTourney, SessionType.Irc })
			ended += await _context.Sessions.EndOpenAsync(type, SessionEndReason.ServerStopped, now);
		return ended;
	}

	/// <summary>Deletes the ended sessions older than the retention period.</summary>
	public async Task<int> PurgeEndedSessionsAsync()
	{
		var retention = (await _context.Settings.GetAsync()).Sessions.RetentionPeriod;
		return await _context.Sessions.DeleteEndedBeforeAsync(_context.Clock.GetUtcNow() - retention);
	}
}
