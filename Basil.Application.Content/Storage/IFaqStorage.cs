namespace Basil.Application.Content.Storage;

public interface IFaqStorage
{
	Task<IEnumerable<string>> ListAsync(CancellationToken ct = default);

	Task<IFaqEntryWriter> CreateOrUpdateAsync(string entry);

	Task<StreamReader?> ReadAsync(string entry, CancellationToken ct = default);

	Task DeleteAsync(string entry);
}