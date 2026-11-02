using Basil.Application.Content.Persistence;
using Microsoft.AspNetCore.Http.Features;

namespace Basil.Host.Rest.Http;

/// <summary>Raises the request size limit of the routes that accept an uploaded file.</summary>
internal static class UploadLimit
{
	/// <summary>Allows the request body to be as large as the largest upload the server accepts.</summary>
	/// <param name="context">The request, before its body is read.</param>
	/// <param name="settings">The runtime settings that hold the upload limits.</param>
	public static async Task AllowAsync(HttpContext context, ISettingsRepository settings)
	{
		var limit = (await settings.GetAsync(context.RequestAborted)).Uploads.OszMaxSize;
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
