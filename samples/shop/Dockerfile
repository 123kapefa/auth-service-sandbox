# Сервис магазина (Orders или Notifications) — как собрал бы его чужой проект: пакеты сервиса входа из фида
# artifacts/nuget (сначала scripts/package/pack.sh). Контекст сборки — корень репозитория:
#   docker build -f samples/shop/Dockerfile --build-arg SERVICE=Orders -t shop-orders:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src/samples/shop
# nuget.config магазина ищет фид в ../../artifacts/nuget — та же раскладка, что в репозитории.
COPY artifacts/nuget /src/artifacts/nuget
COPY samples/shop/ ./
RUN dotnet publish "$SERVICE/$SERVICE.csproj" -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG SERVICE
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080 \
    SERVICE_DLL=${SERVICE}.dll
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$SERVICE_DLL\""]
