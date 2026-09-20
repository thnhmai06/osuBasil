using Basil.Application.Queries;

namespace Basil.Application.Persistence.Repository;

public interface IRecordRepository<T> where T : notnull
{
	Task<IReadOnlyCollection<T>> SearchAsync(
		IQuery<T> query,
		QueryOptions? options = null,
		SortOptions? sortOptions = null,
		CancellationToken cancellationToken = default);

	Task<int> CountAsync(
		IQuery<T> query,
		QueryOptions? options = null,
		SortOptions? sortOptions = null,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyCollection<bool>> CreateOrUpdateAsync(
		IReadOnlySet<T> obj, CancellationToken cancellationToken = default);

	Task<IReadOnlyCollection<bool>> DeleteAsync(
		IReadOnlySet<T> obj, CancellationToken cancellationToken = default);
}
// TODO: Need DatabaseLogRepository (on db)