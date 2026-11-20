using Basil.Application.Shared.Settings;
using Mediator;
using Microsoft.AspNetCore.Http.Features;

namespace Basil.Host.Rest.Http;

internal static class UploadLimit
{
	public static async Task AllowAsync(HttpContext context, ISender sender)
	{
		var limit = (await sender.Send(new ReadSettings(), context.RequestAborted)).Uploads.OszMaxSize;
		Allow(context, limit);
	}

	public static void Allow(HttpContext context, long limit)
	{
		if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
			feature.MaxRequestBodySize = limit;
	}
}