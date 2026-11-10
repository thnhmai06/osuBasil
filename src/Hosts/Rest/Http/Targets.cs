using System.Text.Json;
using Basil.Application.Common;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Host.Rest.Http;

/// <summary>Reads the paging parameters of a list request and the other pieces routes share.</summary>
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

	/// <summary>Gets the sender of messages to the system.</summary>
	public static ISender Sender(this HttpContext context)
	{
		return context.RequestServices.GetRequiredService<ISender>();
	}

	/// <summary>Reads a JSON body that may be absent.</summary>
	/// <returns>
	///     The body, or <see langword="null" /> when the request has no JSON body; the refusal to answer with when the
	///     body is present but is not valid JSON of the expected shape.
	/// </returns>
	public static async Task<(T? Body, IResult? Refusal)> OptionalBodyAsync<T>(this HttpContext context)
		where T : class
	{
		if (!context.Request.HasJsonContentType()) return (null, null);

		try
		{
			return (await context.Request.ReadFromJsonAsync<T>(Api.Json, context.RequestAborted), null);
		}
		catch (JsonException)
		{
			return (null, Api.Invalid("The body is not valid JSON."));
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