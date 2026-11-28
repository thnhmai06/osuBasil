using Basil.Gateway.Admission;

namespace Basil.Host.Rest.Http;

internal sealed class RestAdmission(RequestDelegate next)
{
	private const string HealthPath = "/health";

	public async Task InvokeAsync(HttpContext context, Admission admission)
	{
		// The health probe has to answer exactly when the server is shedding.
		if (context.GetEndpoint() is not { } endpoint || context.Request.Path == HealthPath)
		{
			await next(context);
			return;
		}

		var workClass = AdmissionRefusals.ClassOf(endpoint.Metadata);
		var session = context.ConnectionOrNull();
		var decision = admission.AdmitRest(context.Request, session, workClass);
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
			admission.Release(workClass, session);
		}
	}
}