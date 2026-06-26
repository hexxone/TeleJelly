# Build stage
ARG DOTNET_VERSION="9.0"
ARG JELLYFIN_VERSION="10.11"

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG JELLYFIN_VERSION

WORKDIR /src

# Install Node.js and minification tools
RUN apt-get update && apt-get install -y nodejs npm && \
    npm install -g html-minifier-terser terser clean-css-cli && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# optimized nuget restore
COPY ["TeleJelly.sln", "./"]
COPY ["Jellyfin.Plugin.TeleJelly/Jellyfin.Plugin.TeleJelly.csproj", "Jellyfin.Plugin.TeleJelly/"]
COPY ["JellyfinPluginHelper/JellyfinPluginHelper.csproj", "JellyfinPluginHelper/"]
COPY ["TeleJelly.Tests/TeleJelly.Tests.csproj", "TeleJelly.Tests/"]
RUN dotnet restore

# full copy + build
COPY . .

# Minify Assets in-place before build
RUN find Jellyfin.Plugin.TeleJelly/Assets -name "*.css" -type f -exec sh -c 'cleancss -o "{}" "{}"' \; && \
    find Jellyfin.Plugin.TeleJelly/Assets -name "*.js" -type f -exec sh -c 'terser "{}" -o "{}" -c -m' \; && \
    find Jellyfin.Plugin.TeleJelly/Assets -name "*.html" -type f -exec sh -c 'html-minifier-terser --collapse-whitespace --remove-comments --minify-css --minify-js -o "{}" "{}"' \;

RUN dotnet publish Jellyfin.Plugin.TeleJelly/Jellyfin.Plugin.TeleJelly.csproj -c Release -o /app/publish

# Final stage using official Jellyfin image
FROM jellyfin/jellyfin:${JELLYFIN_VERSION}

COPY --from=build /app/publish/Jellyfin.Plugin.TeleJelly.dll /config/plugins/TeleJelly/
COPY --from=build /src/meta.json /config/plugins/TeleJelly/
