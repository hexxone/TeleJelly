# Build stage
ARG DOTNET_VERSION="8.0"
ARG JELLYFIN_VERSION="10.10"

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG JELLYFIN_VERSION

WORKDIR /src

# optimized nuget restore
COPY ["TeleJelly.sln", "./"]
COPY ["Jellyfin.Plugin.TeleJelly/Jellyfin.Plugin.TeleJelly.csproj", "Jellyfin.Plugin.TeleJelly/"]
COPY ["JellyfinPluginHelper/JellyfinPluginHelper.csproj", "JellyfinPluginHelper/"]
RUN dotnet restore

# full copy + build
COPY . .
RUN dotnet publish Jellyfin.Plugin.TeleJelly/Jellyfin.Plugin.TeleJelly.csproj -c Release -v d -o /app/publish

# Final stage using official Jellyfin image
FROM jellyfin/jellyfin:${JELLYFIN_VERSION}

COPY --from=build /app/publish/Jellyfin.Plugin.TeleJelly.dll /config/plugins/TeleJelly/
COPY --from=build /src/meta.json /config/plugins/TeleJelly/
