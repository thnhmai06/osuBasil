using Basil.Application.Content.Persistence;
using Basil.Host.Rest.Http;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Middleware;

namespace Basil.Host.Rest.Images;

/// <summary>Registers and serves the image hosts: avatars, menu images, screenshots and beatmap images.</summary>
public static class ImageHosts
{
	/// <summary>Adds the image pipeline with its providers; the host names the folder of the file cache.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddRestImages(this IServiceCollection services)
	{
		services.AddImageSharp(options =>
			{
				options.CacheMaxAge = TimeSpan.FromDays(30);
				options.BrowserMaxAge = TimeSpan.FromDays(7);
				options.OnParseCommandsAsync = context =>
				{
					var path = context.Context.Request.Path;
					if (BeatmapImageProvider.TryGetSize(path, out var width, out var height) ||
					    BeatmapThumbnailProvider.TryGetSize(path, out width, out height))
					{
						context.Commands["width"] = width.ToString();
						context.Commands["height"] = height.ToString();
						context.Commands["rmode"] = "crop";
					}

					return Task.CompletedTask;
				};
				options.OnPrepareResponseAsync = context =>
				{
					// Avatars change when users upload again, so they are cached for a shorter time.
					if (context.Request.Host.Host.StartsWith("a.", StringComparison.OrdinalIgnoreCase))
						context.Response.Headers.CacheControl = "public,max-age=300";
					context.Response.Headers.XContentTypeOptions = "nosniff";
					return Task.CompletedTask;
				};
			})
			.ClearProviders()
			.AddProvider<AvatarProvider>()
			.AddProvider<MenuImageProvider>()
			.AddProvider<ScreenshotProvider>()
			.AddProvider<BeatmapImageProvider>()
			.AddProvider<BeatmapThumbnailProvider>();
		return services;
	}

	/// <summary>
	///     Serves the images; add it before the hosts' own steps, because the image middleware runs before routing and the
	///     rate limit of the images has to run before it.
	/// </summary>
	/// <param name="app">The application pipeline.</param>
	/// <returns><paramref name="app" />, for chaining.</returns>
	public static IApplicationBuilder UseRestImages(this IApplicationBuilder app)
	{
		app.UseMiddleware<ImageRateLimitMiddleware>();
		return app.UseImageSharp();
	}
}

/// <summary>Limits how many images one address can fetch; an answer of 304 costs nothing.</summary>
internal sealed class ImageRateLimitMiddleware(RequestDelegate next)
{
	private static readonly HashSet<string> Labels = new(StringComparer.OrdinalIgnoreCase) { "a", "assets", "b" };

	public async Task InvokeAsync(HttpContext context, ISettingsRepository settings, RateLimiter limiter)
	{
		var host = context.Request.Host.Host;
		var label = host.Contains('.') ? host[..host.IndexOf('.')] : host;
		var isImage = Labels.Contains(label) || (label.Equals("osu", StringComparison.OrdinalIgnoreCase) &&
		                                         context.Request.Path.StartsWithSegments("/ss"));
		if (!isImage)
		{
			await next(context);
			return;
		}

		var limit = (await settings.GetAsync(context.RequestAborted)).RateLimits.Rest.Anonymous.Assets;
		var key = $"rest-assets:{context.Ip()}";
		if (limiter.Count(key, limit.Window) >= limit.Count)
		{
			context.Response.Headers.RetryAfter = ((int)limit.Window.TotalSeconds).ToString();
			await Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many requests.").ExecuteAsync(context);
			return;
		}

		await next(context);
		if (context.Response.StatusCode != StatusCodes.Status304NotModified) limiter.Record(key);
	}
}
