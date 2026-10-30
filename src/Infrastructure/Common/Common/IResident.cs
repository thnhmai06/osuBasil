namespace Basil.Infrastructure.Common.Common;

/// <summary>Data every client uses, or storage state readied at startup, loaded once after the database is up to date.</summary>
public interface IResident
{
	/// <summary>Loads the data into memory; called once at startup, after the database is up to date.</summary>
	Task LoadAsync(CancellationToken cancellationToken);
}