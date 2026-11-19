using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Web.DependencyInjection;

namespace Basil.Host.Rest.Images;

public static class ImageHosts
{
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

	public static IApplicationBuilder UseRestImages(this IApplicationBuilder app)
	{
		app.UseMiddleware<ImageAdmissionMiddleware>();
		app.UseMiddleware<BeatmapPreviewMiddleware>();
		app.UseMiddleware<UndecodableImageMiddleware>();
		return app.UseImageSharp();
	}
}

internal sealed class ImageAdmissionMiddleware(RequestDelegate next)
{
	private static readonly HashSet<string> Labels = new(StringComparer.OrdinalIgnoreCase) { "a", "assets", "b" };

	public async Task InvokeAsync(HttpContext context, Admission admission)
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

		var decision = admission.Admit(AdmissionRefusals.SubjectOf(context.Ip().ToString()), WorkClass.Asset);
		if (!decision.Allowed)
		{
			context.Response.Headers.RetryAfter = AdmissionRefusals.RetryAfterOf(decision);
			await Api.Error(AdmissionRefusals.StatusOf(decision), AdmissionRefusals.CodeOf(decision),
				AdmissionRefusals.MessageOf(decision)).ExecuteAsync(context);
			return;
		}

		try
		{
			await next(context);
		}
		finally
		{
			admission.Release(WorkClass.Asset);
		}
	}
}

internal sealed class UndecodableImageMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context)
	{
		try
		{
			await next(context);
		}
		catch (Exception exception) when (exception is InvalidImageContentException or UnknownImageFormatException &&
		                                  !context.Response.HasStarted)
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
		}
	}
}