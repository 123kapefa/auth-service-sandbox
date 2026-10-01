FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/AuthService.Api/AuthService.Api.csproj src/AuthService.Api/
COPY src/AuthService.Contracts/AuthService.Contracts.csproj src/AuthService.Contracts/
COPY src/AuthService.Core/AuthService.Core.csproj src/AuthService.Core/
COPY src/AuthService.Domain/AuthService.Domain.csproj src/AuthService.Domain/
COPY src/AuthService.Infrastructure.Keycloak/AuthService.Infrastructure.Keycloak.csproj src/AuthService.Infrastructure.Keycloak/
COPY src/AuthService.Infrastructure.Postgres/AuthService.Infrastructure.Postgres.csproj src/AuthService.Infrastructure.Postgres/
# Shared-проекты: без их .csproj restore не увидит их пакеты, и publish --no-restore упадёт.
COPY src/Shared/Authentication/Authentication.csproj src/Shared/Authentication/
COPY src/Shared/Authentication.Redis/Authentication.Redis.csproj src/Shared/Authentication.Redis/
COPY src/Shared/Framework/Framework.csproj src/Shared/Framework/
COPY src/Shared/SharedKernel/SharedKernel.csproj src/Shared/SharedKernel/
COPY src/Shared/Validation/Validation.csproj src/Shared/Validation/
RUN dotnet restore src/AuthService.Api/AuthService.Api.csproj
COPY . .
RUN dotnet publish src/AuthService.Api/AuthService.Api.csproj -c Release --no-restore -o /out/api

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AuthService.Api.dll"]
