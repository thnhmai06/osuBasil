using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Basil.Host.Rest.Http;

/// <summary>Describes the API: the Bearer scheme, the envelope every JSON answer uses, and the errors a route can give.</summary>
internal static class OpenApiSetup
{
	private const string SchemeId = "Bearer";

	private const string Overview = """
	                                The HTTP API of Basil. Every route is under `/v1`.

	                                **Authentication.** Routes marked with a lock need `Authorization: Bearer <token>`; the others are public. Sign in with `POST /v1/auth/login`. To learn the id of your own user, call `GET /v1/auth/session`. `/v1/me` and `/v1/sessions` redirect with 307 to the same route under your own user.

	                                **Envelope.** Every JSON answer has the shape `{ success, code, message, data, meta, errors, timestamp }`. A failed answer carries a stable `errors.code`; a refusal for lack of permissions also carries the missing permission mask in `errors.missing`. Images, files and Server-Sent Events are not wrapped.

	                                **Paging.** Lists take `offset` (default 0) and `limit` (default 25, at most 100; more is 422).

	                                **Live data.** Routes marked as streams use Server-Sent Events and need no content negotiation; data is up to about one second old.
	                                """;

	public static IServiceCollection AddBasilOpenApi(this IServiceCollection services)
	{
		services.AddOpenApi("v1", options =>
		{
			options.ShouldInclude = description =>
				description.RelativePath?.StartsWith("v1/", StringComparison.Ordinal) == true;
			options.AddDocumentTransformer((document, _, _) =>
			{
				document.Info = new OpenApiInfo { Title = "Basil API", Version = "v1", Description = Overview };
				document.Components ??= new OpenApiComponents();
				document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
				document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
				{
					Type = SecuritySchemeType.Http,
					Scheme = "bearer",
					Description = "The token `POST /v1/auth/login` returns."
				};
				document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
				document.Components.Schemas["Envelope"] = new OpenApiSchema
				{
					Type = JsonSchemaType.Object,
					Required = new HashSet<string> { "success", "code", "message", "timestamp" },
					Properties = new Dictionary<string, IOpenApiSchema>
					{
						["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
						["code"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
						["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
						["data"] = new OpenApiSchema(),
						["meta"] = new OpenApiSchema
						{
							Type = JsonSchemaType.Object | JsonSchemaType.Null,
							Properties = new Dictionary<string, IOpenApiSchema>
							{
								["offset"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
								["limit"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
								["total"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
							}
						},
						["errors"] = new OpenApiSchema
						{
							Type = JsonSchemaType.Object | JsonSchemaType.Null,
							Properties = new Dictionary<string, IOpenApiSchema>
							{
								["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
								["missing"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null }
							}
						},
						["timestamp"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
					}
				};
				return Task.CompletedTask;
			});
			options.AddOperationTransformer((operation, context, _) =>
			{
				var metadata = context.Description.ActionDescriptor.EndpointMetadata;
				var token = metadata.OfType<TokenRequired>().Any();
				var stream = metadata.OfType<SseRoute>().Any();
				var method = context.Description.HttpMethod ?? "GET";
				operation.Responses ??= new OpenApiResponses();

				OpenApiResponse Json(string description)
				{
					return new OpenApiResponse
					{
						Description = description,
						Content = new Dictionary<string, OpenApiMediaType>
						{
							["application/json"] = new()
								{ Schema = new OpenApiSchemaReference("Envelope", context.Document) }
						}
					};
				}

				// Handlers return the envelope themselves, so the success answer is described here.
				foreach (var code in operation.Responses.Keys.Where(code => code.StartsWith('2')).ToList())
					operation.Responses.Remove(code);
				if (stream)
					operation.Responses["200"] = new OpenApiResponse
					{
						Description = "A Server-Sent Events stream.",
						Content = new Dictionary<string, OpenApiMediaType> { ["text/event-stream"] = new() }
					};
				else
					operation.Responses["200"] = Json("Success.");

				if (token)
				{
					operation.Security ??= [];
					operation.Security.Add(new OpenApiSecurityRequirement
					{
						[new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = []
					});
					operation.Responses["401"] = Json("The token is missing, expired or revoked.");
					operation.Responses["403"] = Json("The caller lacks permissions; `errors.missing` holds the mask.");
				}

				operation.Responses["404"] = Json("The thing does not exist or the caller may not see it.");
				if (method != "GET")
				{
					operation.Responses["409"] =
						Json("The state of the system does not allow it, or an area is locked.");
					operation.Responses["422"] = Json("The input is not valid.");
				}
				else
				{
					operation.Responses["422"] = Json("A paging parameter is out of range.");
				}

				operation.Responses["429"] = Json("Too many requests; `Retry-After` says when to try again.");
				return Task.CompletedTask;
			});
		});
		return services;
	}

	public static void MapDocs(this IEndpointRouteBuilder app)
	{
		app.MapOpenApi().ExcludeFromDescription();
		app.MapScalarApiReference("/docs",
				options => { options.WithTitle("Basil API").WithOpenApiRoutePattern("/openapi/{documentName}.json"); })
			.ExcludeFromDescription();
	}
}