using System.Text.Json;
using Basil.Application.Shared.Paging;

namespace Basil.Host.Rest.Http;

public sealed record Envelope<T>(
	bool Success,
	int Code,
	string Message,
	T? Data,
	PageMeta? Meta,
	ErrorBody? Errors,
	DateTimeOffset Timestamp);

public sealed record ErrorBody(string Code);

public sealed record PageMeta(int Offset, int Limit, bool HasNext);

public static class Api
{
	public const int MaxLimit = 100;

	public static readonly JsonSerializerOptions Json = ApiJson.Options;

	public static IResult Ok<T>(T data, string message = "Retrieval successful")
	{
		return Respond(StatusCodes.Status200OK, message, data, null, null);
	}

	public static IResult Done(string message = "Operation successful")
	{
		return Respond<object>(StatusCodes.Status200OK, message, null, null, null);
	}

	public static IResult Created<T>(T data, string message = "Created successfully")
	{
		return Respond(StatusCodes.Status201Created, message, data, null, null);
	}

	public static IResult Accepted(string message)
	{
		return Respond<object>(StatusCodes.Status202Accepted, message, null, null, null);
	}

	public static IResult Paged<T>(Page<T> page, PageRequest request)
	{
		return Respond(StatusCodes.Status200OK, "Retrieval successful", page.Items,
			new PageMeta(request.Offset, request.Limit, page.HasNext), null);
	}

	public static IResult Error(int status, string code, string message)
	{
		return Respond<object>(status, message, null, null,
			new ErrorBody(code));
	}

	public static IResult NotFound(string message = "Not found")
	{
		return Error(StatusCodes.Status404NotFound, "notFound", message);
	}

	public static IResult Invalid(string message, string code = "invalid")
	{
		return Error(StatusCodes.Status422UnprocessableEntity, code, message);
	}

	public static IResult Unauthorized()
	{
		return Error(StatusCodes.Status401Unauthorized, "unauthorized", "A valid Bearer token is required.");
	}

	private static IResult Respond<T>(int status, string message, T? data, PageMeta? meta, ErrorBody? errors)
	{
		var body = new Envelope<T>(status < 400, status, message, data, meta, errors, DateTimeOffset.UtcNow);
		return Results.Json(body, Json, "application/json", status);
	}
}