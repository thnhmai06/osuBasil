using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Routes;

/// <summary>The aliases of the caller's own account: answered with a redirect that keeps the method and the body.</summary>
internal static class AliasRoutes
{
	public static void MapAliases(this RouteGroupBuilder v1)
	{
		MapAlias(v1, "/me", id => $"/v1/users/{id}");
		MapAlias(v1, "/sessions", id => $"/v1/users/{id}/sessions");
	}

	private static void MapAlias(RouteGroupBuilder v1, string prefix, Func<int, string> target)
	{
		RequestDelegate redirect = context =>
		{
			var connection = context.ConnectionOrNull();
			if (connection is null) return Api.Unauthorized().ExecuteAsync(context);

			var rest = context.Request.RouteValues["rest"] as string;
			var location = target(connection.Record.Id) + (string.IsNullOrEmpty(rest) ? "" : "/" + rest) +
			               context.Request.QueryString;
			return Results.Redirect(location, false, true).ExecuteAsync(context);
		};
		v1.Map(prefix, redirect).WithTags("Aliases")
			.WithSummary("Redirect to the same route under the caller's own account.")
			.ExcludeFromDescription();
		v1.Map(prefix + "/{**rest}", redirect).ExcludeFromDescription();
	}
}