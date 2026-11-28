using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Images;

public static class ImageHosts
{
	public static IApplicationBuilder UseRestImages(this IApplicationBuilder app)
	{
		app.UseMiddleware<ImageAdmissionMiddleware>();
		app.UseMiddleware<BeatmapPreviewMiddleware>();
		app.UseMiddleware<MenuContentMiddleware>();
		return app.UseMiddleware<ImageServingMiddleware>();
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

		var decision = admission.Admit(null, WorkClass.Asset);
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
