FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS base
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

FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
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
