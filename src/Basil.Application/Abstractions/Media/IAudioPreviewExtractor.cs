namespace Basil.Application.Abstractions.Media;

/// <summary>
///     Cuts a fixed-length clip out of a beatmap's audio file and encodes it as mp3 — backs
///     `b.ppy.sh/preview/{beatmapsetId}.mp3` and the `api.` host's audio-preview route. Implemented
///     by shelling out to ffmpeg (FFMpegCore) in Basil.Infrastructure.
/// </summary>
public interface IAudioPreviewExtractor
{
	/// <param name="audioFilePath">Path to the source audio file on disk (the beatmap's full track).</param>
	/// <param name="startMs">Offset into the track to start the clip, in milliseconds.</param>
	/// <param name="duration">Clip length — 10 seconds for every caller today, but not hardcoded here.</param>
	/// <param name="cancellationToken"></param>
	/// <returns>128kbps mp3 bytes.</returns>
	Task<byte[]> ExtractAsync(string audioFilePath, int startMs, TimeSpan duration,
		CancellationToken cancellationToken = default);
}
