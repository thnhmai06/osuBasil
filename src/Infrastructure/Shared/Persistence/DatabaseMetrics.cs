using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Basil.Infrastructure.Shared.Persistence;

/// <summary>Counts the commands sent to the database and times the most recent ones.</summary>
public sealed class DatabaseMetrics : DbCommandInterceptor
{
	private const int Window = 256;
	private readonly Lock _lock = new();
	private readonly double[] _recent = new double[Window];
	private long _commands;
	private long _failed;

	/// <summary>Gets how many commands have run since the server started, failed ones included.</summary>
	public long Commands => Interlocked.Read(ref _commands);

	/// <summary>Gets how many commands failed since the server started.</summary>
	public long Failed => Interlocked.Read(ref _failed);

	/// <summary>Gets the time in milliseconds that a share of the last 256 commands took at most.</summary>
	/// <param name="fraction">The share of commands, from 0 to 1.</param>
	/// <returns>The duration, or 0 when no command has run.</returns>
	public double Percentile(double fraction)
	{
		double[] sorted;
		lock (_lock) sorted = _recent[..(int)Math.Min(_commands, Window)].Order().ToArray();
		return sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(fraction * sorted.Length))];
	}

	/// <inheritdoc />
	public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData,
		DbDataReader result)
	{
		Record(eventData.Duration);
		return result;
	}

	/// <inheritdoc />
	public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
		DbDataReader result, CancellationToken cancellationToken = default)
	{
		Record(eventData.Duration);
		return ValueTask.FromResult(result);
	}

	/// <inheritdoc />
	public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
	{
		Record(eventData.Duration);
		return result;
	}

	/// <inheritdoc />
	public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
		int result, CancellationToken cancellationToken = default)
	{
		Record(eventData.Duration);
		return ValueTask.FromResult(result);
	}

	/// <inheritdoc />
	public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
	{
		Record(eventData.Duration);
		return result;
	}

	/// <inheritdoc />
	public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
		object? result, CancellationToken cancellationToken = default)
	{
		Record(eventData.Duration);
		return ValueTask.FromResult(result);
	}

	/// <inheritdoc />
	public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
	{
		Interlocked.Increment(ref _failed);
		Record(eventData.Duration);
	}

	/// <inheritdoc />
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
