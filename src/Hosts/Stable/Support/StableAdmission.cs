using Basil.Application.Users.Registry;
using Basil.Gateway;

namespace Basil.Host.Stable.Support;

/// <summary>Asks the gateway whether a request of the osu! client may enter and answers 429 or 503 when it may not.</summary>
internal sealed class StableAdmission(Admission admission, UserRegistry registry) : IEndpointFilter
{
	/// <inheritdoc />
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
