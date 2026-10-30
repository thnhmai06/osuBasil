using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Users;

namespace Basil.Host.Rest.Http;

/// <summary>Reads the paging parameters of a list request and finds the account a route names.</summary>
public static class Targets
{
	/// <summary>Reads <c>offset</c> and <c>limit</c>: default 0 and 25, at most 100.</summary>
	/// <returns>The page, or the 422 answer when a value is out of range.</returns>
	public static (PageRequest? Page, IResult? Error) Paging(int? offset, int? limit)
	{
		var o = offset ?? 0;
		var l = limit ?? 25;
		if (o < 0 || l < 1 || l > Api.MaxLimit)
			return (null, Api.Invalid($"offset must be 0 or more and limit between 1 and {Api.MaxLimit}."));

		return (new PageRequest(o, l), null);
	}

	/// <summary>Finds the account with an id and scopes it to the calling connection.</summary>
	/// <returns>The scoped account, or <see langword="null" /> when no such account exists.</returns>
	public static async Task<ScopedUser?> ScopeAsync(this HttpContext context, int id)
	{
		var services = context.RequestServices;
		var record = await services.GetRequiredService<IUserRepository>().GetAsync(id, context.RequestAborted);
		return record is null
			? null
			: services.GetRequiredService<UserRegistry>().Of(record).ScopeTo(context.Connection());
	}

	/// <summary>Reads a JSON body that may be absent.</summary>
	/// <returns>The body, or <see langword="null" /> when the request has no JSON body or it is not valid JSON.</returns>
	public static async Task<T?> OptionalBodyAsync<T>(this HttpContext context) where T : class
	{
		if (!context.Request.HasJsonContentType()) return null;

		try
		{
			return await context.Request.ReadFromJsonAsync<T>(Api.Json, context.RequestAborted);
		}
		catch (System.Text.Json.JsonException)
		{
			return null;
		}
	}

	/// <summary>Reads a two-letter country code.</summary>
	public static Country? ParseCountry(string? code)
	{
		return code is { Length: 2 } && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country)
			? country
			: null;
	}
}
