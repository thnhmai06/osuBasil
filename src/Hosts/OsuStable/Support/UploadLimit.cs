using Basil.Application.Content.Persistence;
using Microsoft.AspNetCore.Http.Features;

namespace Basil.Host.OsuStable.Support;

/// <summary>Raises the request size limit of the osu! web routes that accept an uploaded file.</summary>
internal static class UploadLimit
{
	private const long FormSlack = 1024 * 1024;

	/// <summary>Allows the request body to be as large as the upload of one route plus its form fields.</summary>
	/// <param name="context">The request, before its body is read.</param>
	/// <param name="settings">The runtime settings that hold the upload limits.</param>
	/// <param name="upload">Picks the largest accepted upload of the route from the settings.</param>
	public static async Task AllowAsync(HttpContext context, ISettingsRepository settings,
		Func<ServerSettings.UploadSettings, long> upload)
	{
		// The caller is not known until the form is read, so the room given to an anonymous body is the size of this
		// route's own upload, never the largest upload the server accepts.
		var limit = upload((await settings.GetAsync(context.RequestAborted)).Uploads) + FormSlack;
		Allow(context, limit);
	}

	/// <summary>Allows the request body to be as large as a given size.</summary>
	/// <param name="context">The request, before its body is read.</param>
	/// <param name="limit">The largest body, in bytes.</param>
	public static void Allow(HttpContext context, long limit)
	{
		if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
			feature.MaxRequestBodySize = limit;
	}
}