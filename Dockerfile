# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["TeleJelly.sln", "./"]
COPY ["TeleJelly/TeleJelly.csproj", "TeleJelly/"]
COPY ["JellyfinPluginHelper/JellyfinPluginHelper.csproj", "JellyfinPluginHelper/"]

RUN dotnet restore
COPY . .
RUN dotnet publish TeleJelly/TeleJelly.csproj -c Release -o /app/publish

# Final stage using official Jellyfin image
FROM jellyfin/jellyfin:latest

COPY --from=build /app/publish/TeleJelly.dll /config/plugins/TeleJelly/
COPY --from=build /src/meta.json /config/plugins/TeleJelly/
