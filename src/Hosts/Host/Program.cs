using Basil.Host;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBasil();
await builder.Build().RunAsync();
