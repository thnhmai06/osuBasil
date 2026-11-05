namespace Basil.Application.Beatmaps.Services;

/// <summary>Recomputes the stored metadata of every beatmap, star rating included, with the rulesets in use.</summary>
public interface IBeatmapReloader
{
	/// <summary>Gets a value that indicates whether a reload is running.</summary>
	bool Running { get; }

	/// <summary>Starts a reload in the background.</summary>
	/// <returns><see langword="true" /> when it started; <see langword="false" /> when one is already running.</returns>
	bool TryStart();
}