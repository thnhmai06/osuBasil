using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record UploadScreenshot(Connection Actor, Stream Image)
	: IRequest<(ContentReference? Screenshot, ServerOutcome Result)>;