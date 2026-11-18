namespace Basil.Host.Cli.Dashboard.Data;

public sealed class ExceptionWindow
{
	private const int Shown = 10;
	private static readonly TimeSpan Span = TimeSpan.FromSeconds(60);
	private readonly List<(DateTimeOffset At, string Type)> _seen = [];
	private long _total = -1;

	public IReadOnlyList<ExceptionCount> Update(long total, IReadOnlyList<string> recent, DateTimeOffset now)
	{
		// The first sample is the baseline; a total that fell means the count was reset.
		var fresh = _total < 0 ? 0 : total >= _total ? total - _total : total;
		_total = total;
		var take = (int)Math.Min(fresh, recent.Count);
		foreach (var type in recent.Skip(recent.Count - take)) _seen.Add((now, type));

		_seen.RemoveAll(entry => now - entry.At > Span);
		return
		[
			.. _seen.GroupBy(entry => entry.Type).Select(group => new ExceptionCount(group.Key, group.Count()))
				.OrderByDescending(count => count.Count).ThenBy(count => count.Type, StringComparer.Ordinal).Take(Shown)
		];
	}
}
