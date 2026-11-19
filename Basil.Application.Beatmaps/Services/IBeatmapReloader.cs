namespace Basil.Application.Beatmaps.Services;

public interface IBeatmapReloader
{
	bool Running { get; }

	bool TryStart();
}