using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Skinning;
using Beatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>The understood content of a .osu file, as ppy's osu!lazer libraries read it.</summary>
internal sealed class OsuBeatmapContent(IWorkingBeatmap working)
{
	/// <summary>Gets the osu!lazer working beatmap the decoded difficulty is read through.</summary>
	internal IWorkingBeatmap Working => working;
}

/// <summary>
///     A minimal <see cref="WorkingBeatmap" /> for headless difficulty calculation only: no
///     osu!framework host, audio, or texture access is needed or provided.
/// </summary>
internal sealed class StreamlessWorkingBeatmap(Beatmap beatmap)
	: WorkingBeatmap(beatmap.BeatmapInfo, null)
{
	protected override IBeatmap GetBeatmap()
	{
		return beatmap;
	}

	public override Texture? GetBackground()
	{
		return null;
	}

	protected override Track? GetBeatmapTrack()
	{
		return null;
	}

	protected override ISkin? GetSkin()
	{
		return null;
	}

	public override Stream? GetStream(string storagePath)
	{
		return null;
	}
}