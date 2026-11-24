FROM mcr.microsoft.com/dotnet/aspnet:10.0.15@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# The SPA is served by the API from wwwroot (the csproj publishes client/dist there). Node 24.21.0.
FROM node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS client
WORKDIR /client
COPY ["client/package.json", "client/package-lock.json", "./"]
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/FoodParkDigital.WebApi/FoodParkDigital.WebApi.csproj", "src/FoodParkDigital.WebApi/"]
COPY ["src/FoodParkDigital.Infrastructure/FoodParkDigital.Infrastructure.csproj", "src/FoodParkDigital.Infrastructure/"]
COPY ["src/FoodParkDigital.Application/FoodParkDigital.Application.csproj", "src/FoodParkDigital.Application/"]
COPY ["src/FoodParkDigital.Domain/FoodParkDigital.Domain.csproj", "src/FoodParkDigital.Domain/"]
RUN dotnet restore "src/FoodParkDigital.WebApi/FoodParkDigital.WebApi.csproj"

COPY . .
COPY --from=client /client/dist ./client/dist
WORKDIR "/src/src/FoodParkDigital.WebApi"
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Run as the image's non-root user; local file storage writes under wwwroot/uploads.
RUN mkdir -p /app/wwwroot/uploads && chown -R $APP_UID /app/wwwroot/uploads
USER $APP_UID
ENTRYPOINT ["dotnet", "FoodParkDigital.WebApi.dll"]
