namespace Basil.Infrastructure.Storage;

/// <summary>Data every client uses, held in memory from startup for the server's whole life.</summary>
internal interface IResident
{
	/// <summary>Loads the data into memory; called once at startup, after the database is up to date.</summary>
	Task LoadAsync(CancellationToken cancellationToken);
}
