# Админка доступа (frontend/): собирается отдельно, в образ сервиса попадает готовая статика (wwwroot/admin).
FROM node:22-alpine AS admin
WORKDIR /admin
COPY frontend/package.json frontend/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-fund --no-audit
COPY frontend/ ./
RUN npm run build

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
# Мигратор (efbundle) — в том же образе, что и сервис: его запускает отдельный контейнер до старта сервиса.
# Версия dotnet-ef — как у пакетов EF Core (Directory.Packages.props).
RUN dotnet tool install --global dotnet-ef --version 10.0.7
RUN /root/.dotnet/tools/dotnet-ef migrations bundle \
      --project src/AuthService.Infrastructure.Postgres --startup-project src/AuthService.Infrastructure.Postgres \
      --configuration Release --output /out/api/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
COPY --from=admin /admin/dist ./wwwroot/admin
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AuthService.Api.dll"]
