# syntax=docker/dockerfile:1.7

# ---------------------------------------------------------------- web bundle
# Base images are pinned by digest as well as tag (resolved 2026-11-07).
FROM node:24-alpine@sha256:a34ab6d7f6f4813f4d0c5469d74685604b962fbdbf5793a7bc7b4998ecb867c8 AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---------------------------------------------------------------- api build
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
ARG COMMIT_SHA=unknown
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj src/BreakMeIfYouCan.Api/packages.lock.json src/BreakMeIfYouCan.Api/
COPY src/BreakMeIfYouCan.Evidence/BreakMeIfYouCan.Evidence.csproj src/BreakMeIfYouCan.Evidence/packages.lock.json src/BreakMeIfYouCan.Evidence/
RUN dotnet restore src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj --locked-mode

COPY src/BreakMeIfYouCan.Api/ src/BreakMeIfYouCan.Api/
COPY src/BreakMeIfYouCan.Evidence/ src/BreakMeIfYouCan.Evidence/
COPY --from=web /web/dist/ src/BreakMeIfYouCan.Api/wwwroot/

RUN dotnet publish src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj \
    -c Release -o /app -p:CommitSha=$COMMIT_SHA --no-restore

# ---------------------------------------------------------------- runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BreakMeIfYouCan.Api.dll"]
