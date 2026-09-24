FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/AuthService.Api/AuthService.Api.csproj src/AuthService.Api/
COPY src/AuthService.Contracts/AuthService.Contracts.csproj src/AuthService.Contracts/
COPY src/AuthService.Core/AuthService.Core.csproj src/AuthService.Core/
COPY src/AuthService.Domain/AuthService.Domain.csproj src/AuthService.Domain/
COPY src/AuthService.Infrastructure.Postgres/AuthService.Infrastructure.Postgres.csproj src/AuthService.Infrastructure.Postgres/
COPY src/AuthService.Migrator/AuthService.Migrator.csproj src/AuthService.Migrator/
RUN dotnet restore src/AuthService.Api/AuthService.Api.csproj \
    && dotnet restore src/AuthService.Migrator/AuthService.Migrator.csproj
COPY . .
RUN dotnet publish src/AuthService.Api/AuthService.Api.csproj -c Release --no-restore -o /out/api
RUN dotnet publish src/AuthService.Migrator/AuthService.Migrator.csproj -c Release --no-restore -o /out/migrator

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AuthService.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator .
ENTRYPOINT ["dotnet", "AuthService.Migrator.dll"]
