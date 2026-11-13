FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

RUN dotnet restore backend/KapeldinerService/KapeldinerService.slnx
RUN dotnet restore backend/KapeldinerService/src/KapeldinerService.Web/KapeldinerService.Web.csproj \
    --runtime linux-x64
RUN dotnet publish backend/KapeldinerService/src/KapeldinerService.Web/KapeldinerService.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /out/publish

RUN dotnet tool install --global dotnet-ef --version 10.0.7 \
    && PATH="$PATH:/root/.dotnet/tools" dotnet ef migrations bundle \
       --self-contained \
       --runtime linux-x64 \
       --configuration Release \
       --output /out/efbundle \
       --project backend/KapeldinerService/src/KapeldinerService.Infrastructure.Postgres/KapeldinerService.Infrastructure.Postgres.csproj \
       --startup-project backend/KapeldinerService/src/KapeldinerService.Web/KapeldinerService.Web.csproj \
       --context KapeldinerServiceDbContext

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV HOME=/home/app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R $APP_UID:$APP_UID /home/app
COPY --from=build --chown=$APP_UID:$APP_UID /out/publish .
COPY --from=build --chown=$APP_UID:$APP_UID /out/efbundle ./efbundle
USER $APP_UID
EXPOSE 8080
CMD ["dotnet", "KapeldinerService.Web.dll"]
