using AuthService.Api.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthServiceRegistrations(builder.Configuration);

WebApplication app = builder.Build();

app.UseAuthServicePipeline();

await app.RunAsync();
