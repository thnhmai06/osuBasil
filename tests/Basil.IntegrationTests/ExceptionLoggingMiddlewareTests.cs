using Basil.Application.Formats;
using Basil.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Exercises <see cref="ExceptionLoggingMiddleware" /> directly (no HTTP host needed — just an
///     <see cref="HttpContext" />), covering the carve-out that <see cref="EnvelopeMiddleware" />'s
///     buffering can't reach: a response that has already started writing before the exception is
///     thrown (e.g. an SSE stream mid-flush, a file download mid-copy) must not get a retroactive
///     envelope attempt — it can only propagate, same as on every non-<c>api.</c> host group.
/// </summary>
public class ExceptionLoggingMiddlewareTests
{
	private static HttpContext MakeContext(bool responseStarted, string host = "api.test.local")
	{
		var context = new DefaultHttpContext();
		context.Request.Host = new HostString(host);

		var responseFeature = Substitute.For<IHttpResponseFeature>();
		responseFeature.HasStarted.Returns(responseStarted);
		context.Features.Set(responseFeature);

		return context;
	}

	[Fact]
	public async Task InvokeAsync_ResponseAlreadyStarted_RethrowsWithoutWritingEnvelope()
	{
		var context = MakeContext(responseStarted: true);
		var logger = LoggerFactory.Create(_ => { }).CreateLogger<ExceptionLoggingMiddleware>();
		var middleware = new ExceptionLoggingMiddleware(_ => throw new InvalidOperationException("boom"), logger);

		await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

		// No envelope attempt: status code and content type are left exactly as a response that
		// already started writing would have them (untouched by this middleware).
		Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
		Assert.Null(context.Response.ContentType);
	}

	[Fact]
	public async Task InvokeAsync_ResponseNotStarted_WritesEnvelopeInstead()
	{
		var context = MakeContext(responseStarted: false);
		context.Response.Body = new MemoryStream();
		var logger = LoggerFactory.Create(_ => { }).CreateLogger<ExceptionLoggingMiddleware>();
		var middleware = new ExceptionLoggingMiddleware(_ => throw new InvalidOperationException("boom"), logger);

		await middleware.InvokeAsync(context);

		Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
		Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
	}
}