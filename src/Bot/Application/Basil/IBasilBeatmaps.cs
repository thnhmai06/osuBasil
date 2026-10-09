using Basil.Domain.Beatmaps;

namespace Basil.Bot.Application.Basil;

/// <summary>Looks up the server's beatmaps.</summary>
public interface IBasilBeatmaps
{
	/// <summary>Gets a beatmap by id.</summary>
	/// <param name="id">The beatmap id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmap, or <see langword="null" /> when the server has none with that id.</returns>
	ValueTask<Beatmap?> GetAsync(int id, CancellationToken cancellationToken = default);
}

