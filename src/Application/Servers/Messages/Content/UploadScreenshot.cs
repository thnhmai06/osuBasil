using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record UploadScreenshot(Session Actor, Stream Image)
	: IRequest<(ContentReference? Screenshot, ServerOutcome Result)>;