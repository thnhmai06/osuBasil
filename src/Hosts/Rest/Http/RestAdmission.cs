using Basil.Gateway;
using Basil.Gateway.Admission;

namespace Basil.Host.Rest.Http;

/// <summary>Asks the gateway whether the request may enter, as the kind of work its route declared, and reports a refusal.</summary>
internal sealed class RestAdmission(RequestDelegate next)
{
	private const string HealthPath = "/health";

	/// <summary>Runs the step for one routed request.</summary>
	public async Task InvokeAsync(HttpContext context, Admission admission)
	{
		// The health probe has to answer exactly when the server is shedding.
		if (context.GetEndpoint() is not { } endpoint || context.Request.Path == HealthPath)
		{
			await next(context);
			return;
		}

		var workClass = AdmissionRefusals.ClassOf(endpoint.Metadata);
		var subject = context.ConnectionOrNull() is { } connection
			? AdmissionRefusals.SubjectOf(connection)
			: AdmissionRefusals.SubjectOf(context.Ip().ToString());
		var decision = admission.Admit(subject, workClass);
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
			admission.Release(workClass);
		}
	}
}