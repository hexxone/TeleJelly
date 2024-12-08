# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["TeleJelly.sln", "./"]
COPY ["Jellyfin.Plugin.TeleJelly/Jellyfin.Plugin.TeleJelly.csproj", "Jellyfin.Plugin.TeleJelly/"]
COPY ["JellyfinPluginHelper/JellyfinPluginHelper.csproj", "JellyfinPluginHelper/"]

RUN dotnet restore
COPY . .
RUN dotnet publish Jellyfin.Plugin.TeleJelly/Jellyfin.Plugin.TeleJelly.csproj -c Release -o /app/publish

# Final stage using official Jellyfin image
FROM jellyfin/jellyfin:latest

COPY --from=build /app/publish/Jellyfin.Plugin.TeleJelly.dll /config/plugins/TeleJelly/
COPY --from=build /src/meta.json /config/plugins/TeleJelly/
