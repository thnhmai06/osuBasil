using Basil.Application.Beatmaps.Storage;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Basil.Infrastructure.Beatmaps.Storage;

/// <summary>Makes each fixed-size variant of a background once, on its first request, and keeps it beside the set.</summary>
internal sealed class BeatmapsetCovers
{
	private const int MaxSourceSide = 8192;

	private const long MaxSourcePixels = 4096L * 4096;

	private const int JpegQuality = 85;

	// Only PNG and JPEG are decoded (see the ImageSharp note in Directory.Build.props): backgrounds come from uploaded archives.
	private static readonly DecoderOptions Decoding = new()
	{
		Configuration = new Configuration(new PngConfigurationModule(), new JpegConfigurationModule()), MaxFrames = 1
	};

	private readonly SingleFlight<string, bool> _flights = new();

	// Decoding a large background is costly in memory, so only a few run at once across all sets.
	private readonly SemaphoreSlim _decoding = new(Math.Max(1, Environment.ProcessorCount / 2));

	public async Task<string?> EnsureAsync(string source, string folder, BeatmapsetCoverVariant variant,
		CancellationToken ct)
	{
		var target = Path.Combine(folder, variant.ToString().ToLowerInvariant() + ".jpg");
		// A variant older than its source belongs to an earlier background.
		if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source)) return target;

		var made = await _flights.RunAsync(target, async () =>
		{
			await _decoding.WaitAsync();
			try
			{
				return await MakeAsync(source, target, variant);
			}
			finally
			{
				_decoding.Release();
			}
		}, ct);
		return made ? target : null;
	}

	private static async Task<bool> MakeAsync(string source, string target, BeatmapsetCoverVariant variant)
	{
		var (width, height) = SizeOf(variant);
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		var temporary = TempFiles.Name(target);
		try
		{
			await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
				             FileOptions.Asynchronous | FileOptions.SequentialScan))
			{
				var info = await Image.IdentifyAsync(Decoding, input);
				if (info.Width > MaxSourceSide || info.Height > MaxSourceSide ||
				    (long)info.Width * info.Height > MaxSourcePixels) return false;

				input.Position = 0;
				using var image = await Image.LoadAsync(Decoding, input);
				image.Mutate(operation => operation.Resize(new ResizeOptions
				{
					Size = new Size(width, height), Mode = ResizeMode.Crop
				}));
				await image.SaveAsJpegAsync(temporary, new JpegEncoder { Quality = JpegQuality });
			}

			File.Move(temporary, target, true);
			return true;
		}
		catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException
			                                  or NotSupportedException)
		{
			return false;
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private static (int Width, int Height) SizeOf(BeatmapsetCoverVariant variant)
	{
		return variant switch
		{
			BeatmapsetCoverVariant.Cover => (900, 250),
			BeatmapsetCoverVariant.Card => (400, 140),
			BeatmapsetCoverVariant.List => (150, 150),
			BeatmapsetCoverVariant.SlimCover => (1920, 360),
			BeatmapsetCoverVariant.Thumbnail => (80, 60),
			_ => (160, 120)
		};
	}
}
