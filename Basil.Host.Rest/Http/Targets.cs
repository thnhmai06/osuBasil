using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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

		/// <summary>Reads a required JSON object body; a missing or mistyped field is a 422 that names it.</summary>
		public async Task<(object? Body, IResult? Refusal)> ReadBodyAsync(Type type)
		{
			if (!context.Request.HasJsonContentType()) return (null, Api.Invalid("A JSON body is required."));

			try
			{
				var node = await JsonNode.ParseAsync(context.Request.Body,
					new JsonNodeOptions { PropertyNameCaseInsensitive = true },
					cancellationToken: context.RequestAborted);
				if (node is not JsonObject body) return (null, Api.Invalid("The body must be a JSON object."));

				if (type == typeof(JsonObject)) return (body, null);

				foreach (var field in RequiredOf(type))
					if (body[field] is null)
						return (null, Api.Invalid($"{field} is required."));

				return (body.Deserialize(type, Api.Json), null);
			}
			catch (JsonException error)
			{
				return (null, Api.Invalid($"{FieldOf(error)} is not valid."));
			}
		}

		public async Task<(T? Body, IResult? Refusal)> ReadBodyAsync<T>()
			where T : class
		{
			var (body, refusal) = await context.ReadBodyAsync(typeof(T));
			return ((T?)body, refusal);
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

	private static readonly ConcurrentDictionary<Type, string[]> RequiredFields = new();

	// The constructor parameters that have no "no value" form: non-nullable values and non-nullable references.
	private static string[] RequiredOf(Type type)
	{
		return RequiredFields.GetOrAdd(type, t =>
		{
			var context = new NullabilityInfoContext();
			return
			[
				.. t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters()
					.Where(p => p.ParameterType.IsValueType
						? Nullable.GetUnderlyingType(p.ParameterType) is null
						: context.Create(p).WriteState == NullabilityState.NotNull)
					.Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!))
			];
		});
	}

	private static string FieldOf(JsonException error)
	{
		var path = error.Path?.TrimStart('$', '.');
		return string.IsNullOrEmpty(path) ? "The body" : path.Split('.', '[')[0];
	}

	public static Country? ParseCountry(string? code)
	{
		return code is { Length: 2 } && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country)
			? country
			: null;
	}
}