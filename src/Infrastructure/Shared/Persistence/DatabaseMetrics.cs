using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Basil.Infrastructure.Shared.Persistence;

public sealed class DatabaseMetrics : DbCommandInterceptor
{
	private const int Window = 256;
	private readonly Lock _lock = new();
	private readonly double[] _recent = new double[Window];
	private long _commands;
	private long _failed;

	public long Commands
	{
		get
		{
			lock (_lock) return _commands;
		}
	}

	public long Failed => Interlocked.Read(ref _failed);

	public double Percentile(double fraction)
	{
		double[] sorted;
		lock (_lock) sorted = _recent[..(int)Math.Min(_commands, Window)].Order().ToArray();
		return sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(fraction * sorted.Length))];
	}

	public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData,
		DbDataReader result)
	{
		Record(eventData.Duration);
		return result;
	}

	public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
		DbDataReader result, CancellationToken cancellationToken = default)
	{
		Record(eventData.Duration);
		return ValueTask.FromResult(result);
	}

	public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
	{
		Record(eventData.Duration);
		return result;
	}

	public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
		int result, CancellationToken cancellationToken = default)
	{
		Record(eventData.Duration);
		return ValueTask.FromResult(result);
	}

	public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
	{
		Record(eventData.Duration);
		return result;
	}

	public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
		object? result, CancellationToken cancellationToken = default)
	{
		Record(eventData.Duration);
		return ValueTask.FromResult(result);
	}

	public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
	{
		Interlocked.Increment(ref _failed);
		Record(eventData.Duration);
	}

	public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
		CancellationToken cancellationToken = default)
	{
		CommandFailed(command, eventData);
		return Task.CompletedTask;
	}

	private void Record(TimeSpan duration)
	{
		lock (_lock) _recent[_commands++ % Window] = duration.TotalMilliseconds;
	}
}
