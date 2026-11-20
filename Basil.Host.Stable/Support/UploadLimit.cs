using Basil.Application.Shared.Settings;
using Mediator;
using Microsoft.AspNetCore.Http.Features;

namespace Basil.Host.Stable.Support;

internal static class UploadLimit
{
	private const long FormSlack = 1024 * 1024;

	public static async Task AllowAsync(HttpContext context, ISender sender,
		Func<ServerSettings.UploadSettings, long> upload)
	{
		// The caller is not known until the form is read, so the room given to an anonymous body is the size of this
		// route's own upload, never the largest upload the server accepts.
		var limit = upload((await sender.Send(new ReadSettings(), context.RequestAborted)).Uploads) + FormSlack;
		Allow(context, limit);
	}

	public static void Allow(HttpContext context, long limit)
	{
		if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
			feature.MaxRequestBodySize = limit;
	}
}