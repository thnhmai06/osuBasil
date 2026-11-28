using NAudio.Wave;

namespace Basil.Infrastructure.Beatmaps.Adapters;

/// <summary>
/// Applies a linear fade-out to a sample provider, starting at a fixed
/// time and ending after a fixed duration.
/// </summary>
internal sealed class FadeOutSampleProvider : ISampleProvider
{
	private readonly ISampleProvider _source;
	private readonly int _channels;
	private readonly int _fadeStartFrame;
	private readonly int _fadeEndFrame;
	private long _positionSamples;

	public FadeOutSampleProvider(ISampleProvider source, TimeSpan fadeStart, TimeSpan fadeDuration)
	{
		_source = source;
		WaveFormat = source.WaveFormat;
		_channels = source.WaveFormat.Channels;

		var sampleRate = source.WaveFormat.SampleRate;
		_fadeStartFrame = (int)(fadeStart.TotalSeconds * sampleRate);
		_fadeEndFrame = _fadeStartFrame + (int)(fadeDuration.TotalSeconds * sampleRate);
	}

	public WaveFormat WaveFormat { get; }

	public int Read(float[] buffer, int offset, int count)
	{
		return Read(buffer.AsSpan(offset, count));
	}

	public int Read(Span<float> buffer)
	{
		var read = _source.Read(buffer);
		for (var i = 0; i < read; i++)
		{
			var frame = (_positionSamples + i) / _channels;
			buffer[i] *= Gain(frame);
		}

		_positionSamples += read;
		return read;
	}

	private float Gain(long frame)
	{
		if (frame <= _fadeStartFrame) return 1f;
		if (frame >= _fadeEndFrame) return 0f;
		var t = (frame - _fadeStartFrame) / (float)(_fadeEndFrame - _fadeStartFrame);
		return 1f - t;
	}
}
