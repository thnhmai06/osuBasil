using Basil.Host.Shared.Http;
using Basil.Gateway.Admission;

namespace Basil.Host.Stable.Support;

internal sealed class StableAdmission(Admission admission) : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation,
		EndpointFilterDelegate next)
	{
		var context = invocation.HttpContext;
		var workClass = AdmissionRefusals.ClassOf(context.GetEndpoint()?.Metadata ?? []);
		var token = context.Request.Headers["osu-token"].ToString();
		var admitted = admission.AdmitStable(token,
			HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/", workClass,
			context.Request.ContentLength ?? 0);
		if (!admitted.Decision.Allowed)
		{
			context.Response.Headers.RetryAfter = AdmissionRefusals.RetryAfterOf(admitted.Decision);
			return Results.StatusCode(AdmissionRefusals.StatusOf(admitted.Decision));
		}

		try
		{
			return await next(invocation);
		}
		finally
		{
			admission.Release(admitted.Class, admitted.Session);
		}
	}
}