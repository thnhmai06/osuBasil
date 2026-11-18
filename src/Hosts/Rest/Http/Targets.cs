using System.Text.Json;
using Basil.Application.Shared.Paging;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Host.Rest.Http;

public static class Targets
{
	public static (PageRequest? Page, IResult? Error) Paging(int? offset, int? limit)
	{
		var o = offset ?? 0;
		var l = limit ?? 25;
		if (o < 0 || l < 1 || l > Api.MaxLimit)
			return (null, Api.Invalid($"offset must be 0 or more and limit between 1 and {Api.MaxLimit}."));

		return (new PageRequest(o, l), null);
	}

	extension(HttpContext context)
	{
		public ISender Sender()
		{
			return context.RequestServices.GetRequiredService<ISender>();
		}

		public async Task<(T? Body, IResult? Refusal)> OptionalBodyAsync<T>()
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
	}

	public static Country? ParseCountry(string? code)
	{
		return code is { Length: 2 } && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country)
			? country
			: null;
	}
}