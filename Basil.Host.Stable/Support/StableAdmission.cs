using Basil.Application.Sessions.Registry;
using Basil.Gateway.Admission;

namespace Basil.Host.Stable.Support;

internal sealed class StableAdmission(Admission admission, SessionRegistry registry) : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation,
		EndpointFilterDelegate next)
	{
		var context = invocation.HttpContext;
		var workClass = AdmissionRefusals.ClassOf(context.GetEndpoint()?.Metadata ?? []);
		var token = context.Request.Headers["osu-token"].ToString();
		var subject = token.Length > 0 && registry.Find(token) is { } connection
			? AdmissionRefusals.SubjectOf(connection)
			: AdmissionRefusals.SubjectOf(ClientAddress.Resolve(context).ToString());
		var decision = admission.Admit(subject, workClass);
		if (!decision.Allowed)
		{
			context.Response.Headers.RetryAfter = AdmissionRefusals.RetryAfterOf(decision);
			return Results.StatusCode(AdmissionRefusals.StatusOf(decision));
		}

		try
		{
			return await next(invocation);
		}
		finally
		{
			admission.Release(workClass);
		}
	}
}