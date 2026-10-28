using Basil.Host;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Basil:Server"));
builder.Services.AddBasil();
var app = builder.Build();
app.UseTrustedProxies();
await app.RunAsync();
