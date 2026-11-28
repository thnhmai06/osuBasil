namespace Basil.Infrastructure.Shared.Registry;

public interface IResident
{
	Task LoadAsync(CancellationToken ct);
}