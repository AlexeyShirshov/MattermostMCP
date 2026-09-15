FROM mattermost/mattermost-preview:7.10.4

# Локальный preview-образ покрывает per-team threads API, которое использует v11-режим.
# Держим конфиг и данные на volume, чтобы стенд переживал пересоздание контейнера.
ENV MM_SERVICESETTINGS_ENABLEOPENSERVER=true \
    MM_TEAMSETTINGS_ENABLEOPENSERVER=true

EXPOSE 8065
