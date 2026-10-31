using Basil.Infrastructure.Common.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Testing.Persistence;

/// <summary>
///     A reader whose next query takes its snapshot of the database as usual but delivers its answer only when the
///     test lets it, so a test can commit a change between a query's snapshot and its answer.
/// </summary>
public sealed class GatedDatabaseReader(Database database, ReadGate gate) : DatabaseReader(database)
{
	/// <summary>Replaces the reader of a composition with one that stops at a gate.</summary>
	/// <param name="gate">The gate the reader waits at.</param>
	/// <returns>The change to give to <see cref="StorageComposition" />.</returns>
	public static Action<IServiceCollection> At(ReadGate gate)
	{
		return services => services.Replace(ServiceDescriptor.Singleton<DatabaseReader>(provider =>
			new GatedDatabaseReader(provider.GetRequiredService<Database>(), gate)));
	}

	/// <inheritdoc />
	public override async Task<T> ReadAsync<TContext, T>(Func<TContext, Task<T>> query,
		CancellationToken cancellationToken = default)
	{
		var answer = await base.ReadAsync(query, cancellationToken);
		await gate.PassAsync(cancellationToken);
		return answer;
	}
}

/// <summary>Holds back the answer of the first query that runs after the gate is armed.</summary>
public sealed class ReadGate
{
	private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _armed;

	/// <summary>Makes the next query stop with its answer in hand.</summary>
	public void Arm()
	{
		Volatile.Write(ref _armed, 1);
	}

	/// <summary>Waits until a query has its answer and is held back.</summary>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	public Task ReachedAsync(CancellationToken cancellationToken)
	{
		return _reached.Task.WaitAsync(cancellationToken);
	}

	/// <summary>Lets the held query deliver its answer.</summary>
	public void Release()
	{
		_released.TrySetResult();
	}

	internal async Task PassAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _armed, 0) == 0)
			return;

		_reached.TrySetResult();
		await _released.Task.WaitAsync(cancellationToken);
	}
}