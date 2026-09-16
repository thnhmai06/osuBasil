using Basil.Server.Shared.Media.Assets;
using SixLabors.ImageSharp.Web.Caching;
using SixLabors.ImageSharp.Web.DependencyInjection;

namespace Basil.Server.Host;

/// <summary>Registers ImageSharp.Web for the host.</summary>
internal static class ImageSharpSetup
{
	/// <summary>
	///     Registers ImageSharp.Web, the request pipeline backing `assets.`'s (and, once migrated,
	///     `b.`/`a.`'s) image serving.
	/// </summary>
	/// <remarks>
	///     Providers are added incrementally as each asset family migrates (menu images, beatmap
	///     thumbnails, and avatars now; beatmapset covers later), each one gated to its own host via
	///     <see cref="AssetsHost.Matches" />. The cache lives under <c>Data/Cache/imagesharp/</c>,
	///     alongside the existing (audio-preview-only now that beatmap thumbnails have migrated)
	///     <c>Data/Cache/</c> folder.
	/// </remarks>
	/// <param name="builder">The web application builder whose ImageSharp.Web pipeline is configured.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		builder.Services.AddImageSharp(options =>
			{
				options.CacheMaxAge = TimeSpan.FromDays(30);
				options.BrowserMaxAge = TimeSpan.FromDays(7);

				// b.<domain>/thumb/{id}.jpg carries no query string (the real osu! client hardcodes the
				// path), so the fixed crop size is injected here rather than read from the request.
				options.OnParseCommandsAsync = ctx =>
				{
					var matched = BeatmapThumbnailProvider.TryGetSize(ctx.Context.Request.Path, out var width,
						out var height);
					if (!matched)
						matched = BeatmapsetBackgroundProvider.TryGetCoverSize(ctx.Context.Request.Path,
							out width, out height);

					if (matched)
					{
						ctx.Commands["width"] = width.ToString();
						ctx.Commands["height"] = height.ToString();
						ctx.Commands["rmode"] = "crop";
					}

					return Task.CompletedTask;
				};

				// Avatars change with user behavior (re-uploads), so they get a much shorter browser
				// cache lifetime than the mostly-static menu/beatmap images above.
				options.OnPrepareResponseAsync = context =>
				{
					if (context.Request.Host.Host.StartsWith("a.", StringComparison.OrdinalIgnoreCase))
						context.Response.Headers.CacheControl = "public,max-age=300";

					return Task.CompletedTask;
				};
			})
			.Configure<PhysicalFileSystemCacheOptions>(o =>
			{
				o.CacheRootPath = Path.Combine(AppContext.BaseDirectory, "Data", "Cache");
				o.CacheFolder = "imagesharp";
			})
			.ClearProviders()
			.AddProvider<MenuSeasonalsProvider>()
			.AddProvider<MenuBannersProvider>()
			.AddProvider<MenuIconProvider>()
			.AddProvider<BeatmapThumbnailProvider>()
			.AddProvider<AvatarProvider>()
			.AddProvider<BeatmapsetBackgroundProvider>();
	}
}
