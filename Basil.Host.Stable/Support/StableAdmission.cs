using Basil.Host.Shared.Http;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Sessions;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;

namespace Basil.Host.Stable.Support;

internal sealed class StableAdmission(Admission admission, SessionRegistry registry, TimeProvider clock) : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation,
		EndpointFilterDelegate next)
	{
		var context = invocation.HttpContext;
		var workClass = AdmissionRefusals.ClassOf(context.GetEndpoint()?.Metadata ?? []);
		var token = context.Request.Headers["osu-token"].ToString();
		var connection = token.Length > 0 ? registry.Find(token) : null;
		var subject = connection is not null
			? AdmissionRefusals.SubjectOf(connection, clock.GetUtcNow())
			: (AdmissionSubject?)null;
		// The bancho poll of an open client is its own traffic, counted apart under a higher rate, so a multiplayer round cannot lock it out;
		// the sign-in (no or unknown token) and the osu-web endpoints keep the rate limit.
		if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/" &&
		    connection is { IsOpen: true, Type: SessionType.Stable or SessionType.StableTourney })
			workClass = WorkClass.Poll;
		var decision = admission.Admit(subject, workClass, context.Request.ContentLength ?? 0);
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
			admission.Release(workClass, subject);
		}
	}
}