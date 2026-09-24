using AuthService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPostgresInfrastructure(builder.Configuration, builder.Environment);

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
await dbContext.Database.MigrateAsync();
