using System.Text.Json;
using System.Text.Json.Serialization;
using Basil.Application.Common;
using Basil.Domain.Users;
using Basil.Host.Shared;

namespace Basil.Host.Rest.Http;

/// <summary>The envelope every JSON response of the API uses.</summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <param name="Success">Whether <paramref name="Code" /> is below 400.</param>
/// <param name="Code">The HTTP status code.</param>
/// <param name="Message">A human-readable description.</param>
/// <param name="Data">The payload, or <see langword="null" />.</param>
/// <param name="Meta">Pagination information, or <see langword="null" />.</param>
/// <param name="Errors">The machine-readable error, or <see langword="null" />.</param>
/// <param name="Timestamp">When the response was produced.</param>
public sealed record Envelope<T>(bool Success, int Code, string Message, T? Data, PageMeta? Meta, ErrorBody? Errors,
	DateTimeOffset Timestamp);

/// <summary>The machine-readable part of an error response.</summary>
/// <param name="Code">The stable camelCase error code.</param>
/// <param name="Missing">The permissions the caller lacks, when the refusal is for lack of permissions.</param>
public sealed record ErrorBody(string Code, ulong? Missing = null);

/// <summary>Pagination information of a list response.</summary>
/// <param name="Offset">The index of the first item.</param>
/// <param name="Limit">The page size asked for.</param>
/// <param name="Total">How many items match in all.</param>
public sealed record PageMeta(int Offset, int Limit, int Total);

/// <summary>Builds the results of the API: always inside the envelope.</summary>
public static class Api
{
	/// <summary>The serializer options of the API: camelCase, enums as camelCase strings.</summary>
	public static readonly JsonSerializerOptions Json = ApiJson.Options;

	/// <summary>The largest page a client may ask for.</summary>
	public const int MaxLimit = 100;

	/// <summary>Answers 200 with a payload.</summary>
	public static IResult Ok<T>(T data, string message = "Retrieval successful")
	{
		return Respond(StatusCodes.Status200OK, message, data, null, null);
	}

	/// <summary>Answers 200 with no payload.</summary>
	public static IResult Done(string message = "Operation successful")
	{
		return Respond<object>(StatusCodes.Status200OK, message, null, null, null);
	}

	/// <summary>Answers 201 with a payload.</summary>
	public static IResult Created<T>(T data, string message = "Created successfully")
	{
		return Respond(StatusCodes.Status201Created, message, data, null, null);
	}

	/// <summary>Answers 200 with a page of items.</summary>
	public static IResult Paged<T>(Page<T> page, PageRequest request)
	{
		return Respond(StatusCodes.Status200OK, "Retrieval successful", page.Items,
			new PageMeta(request.Offset, request.Limit, page.Total), null);
	}

	/// <summary>Answers an error.</summary>
	public static IResult Error(int status, string code, string message, UserPermissions? missing = null)
	{
		return Respond<object>(status, message, null, null,
			new ErrorBody(code, missing is { } m ? (ulong)m : null));
	}

	/// <summary>Answers 404.</summary>
	public static IResult NotFound(string message = "Not found")
	{
		return Error(StatusCodes.Status404NotFound, "notFound", message);
	}

	/// <summary>Answers 422 for input that is not valid.</summary>
	public static IResult Invalid(string message, string code = "invalid")
	{
		return Error(StatusCodes.Status422UnprocessableEntity, code, message);
	}

	/// <summary>Answers 401.</summary>
	public static IResult Unauthorized()
	{
		return Error(StatusCodes.Status401Unauthorized, "unauthorized", "A valid Bearer token is required.");
	}

	/// <summary>Builds a result with the status and body of an envelope.</summary>
	private static IResult Respond<T>(int status, string message, T? data, PageMeta? meta, ErrorBody? errors)
	{
		var body = new Envelope<T>(status < 400, status, message, data, meta, errors, DateTimeOffset.UtcNow);
		return Results.Json(body, Json, "application/json", status);
	}
}
