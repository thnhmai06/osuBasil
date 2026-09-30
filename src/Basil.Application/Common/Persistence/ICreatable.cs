using Basil.Domain.Utilities;

namespace Basil.Application.Common.Persistence;

/// <summary>Stores new items whose identity is assigned by the store.</summary>
/// <remarks>
///     Once <see cref="AddAsync" /> completes, the item is durable: every later read by its
///     assigned identity is guaranteed to see it.
/// </remarks>
/// <typeparam name="TData">The data of a new item, without an identity.</typeparam>
/// <typeparam name="TValue">The stored item, pairing its assigned identity with its data.</typeparam>
public interface ICreatable<in TData, TValue> where TData : notnull where TValue : IWrapper<TData>
{
	/// <summary>Stores a new item and assigns its identity.</summary>
	/// <param name="data">The data of the item to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>The stored item, carrying <paramref name="data" /> and its newly assigned identity.</returns>
	Task<TValue> AddAsync(TData data, CancellationToken cancellationToken = default);
}
