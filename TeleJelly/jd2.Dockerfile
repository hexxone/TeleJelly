# for more info see: https://github.com/hexxone/jdownloader2

FROM debian:bookworm-slim

# Set workdir
WORKDIR /jdownloader

# Install required packages
RUN apt-get update -y && \
    apt-get install -y --no-install-recommends \
        default-jre \
        wget && \
    wget http://installer.jdownloader.org/JDownloader.jar && \
    apt-get remove -y wget && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Volume for downloads and config
VOLUME ["/root/Downloads", "/jdownloader/cfg"]

# Create startup script
RUN echo '#!/bin/bash\nchmod 777 JDownloader.jar\nexec java -Djava.awt.headless=true -jar JDownloader.jar -norestart' > /start.sh && \
    chmod +x /start.sh

# Start with the script
ENTRYPOINT ["/start.sh"]

LABEL org.opencontainers.image.title="JDownloader2"
LABEL org.opencontainers.image.description="JDownloader2 running in headless mode"
# TODO get MinVer
LABEL org.opencontainers.image.version="1.0.0"
LABEL org.opencontainers.image.authors="hexx.one <5312542+hexxone@users.noreply.github.com>"
LABEL org.opencontainers.image.url="https://github.com/hexxone/jdownloader2"
LABEL org.opencontainers.image.source="https://github.com/hexxone/jdownloader2"
LABEL org.opencontainers.image.vendor="hexx.one"
LABEL org.opencontainers.image.licenses="MIT"
LABEL org.opencontainers.image.ref.name="jdownloader2"
LABEL org.opencontainers.image.base.name="debian:bookworm-slim"
