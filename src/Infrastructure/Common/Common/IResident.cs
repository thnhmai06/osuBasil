namespace Basil.Infrastructure.Common;

/// <summary>Data every client uses, loaded into memory at startup and kept there.</summary>
public interface IResident
{
	/// <summary>Loads the data into memory; called once at startup, after the database is up to date.</summary>
	Task LoadAsync(CancellationToken cancellationToken);
}