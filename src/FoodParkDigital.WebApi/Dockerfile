FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# The SPA is served by the API from wwwroot (the csproj publishes client/dist there).
FROM node:20-alpine AS client
WORKDIR /client
COPY ["client/package.json", "client/package-lock.json", "./"]
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
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
