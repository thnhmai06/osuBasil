namespace Basil.Application.Beatmaps.Services;

public interface IBeatmapReloader
{
	bool Running { get; }

	/// <summary>Starts calculating the difficulty of beatmaps again; false when it is already running.</summary>
	/// <param name="onlyMissing">True to calculate only the beatmaps that have no difficulty yet.</param>
	bool TryStart(bool onlyMissing);
}