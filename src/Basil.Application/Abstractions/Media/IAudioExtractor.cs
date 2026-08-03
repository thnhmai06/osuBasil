namespace Basil.Application.Abstractions.Media;

/// <summary>
///     Cuts a fixed-length clip out of a beatmap's audio file and encodes it as mp3.
/// </summary>
/// <remarks>
///     Backs the beatmap audio-preview routes, both the <c>b.&lt;domain&gt;</c> preview handler and
///     the <c>api.</c> host's audio-preview route.
/// </remarks>
public interface IAudioExtractor
{
	/// <summary>
	///     Extracts a clip from the given audio file and returns it as mp3 bytes.
	/// </summary>
	/// <param name="audioFilePath">The path to the source audio file on disk, the beatmap's full track.</param>
	/// <param name="startMs">The offset into the track where the clip starts, in milliseconds.</param>
	/// <param name="duration">The length of the clip to extract.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The extracted clip encoded as 128kbps mp3 bytes.</returns>
	/// <remarks>
	///     Every caller today requests a 10-second clip, but the duration is a parameter, not
	///     hardcoded here. The start offset is clamped to 0 when negative.
	/// </remarks>
	Task<byte[]> ExtractAsync(string audioFilePath, int startMs, TimeSpan duration,
		CancellationToken cancellationToken = default);
}