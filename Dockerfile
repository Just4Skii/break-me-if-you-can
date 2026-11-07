# syntax=docker/dockerfile:1.7

# ---------------------------------------------------------------- web bundle
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---------------------------------------------------------------- api build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG COMMIT_SHA=unknown
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj src/BreakMeIfYouCan.Api/
COPY src/BreakMeIfYouCan.Evidence/BreakMeIfYouCan.Evidence.csproj src/BreakMeIfYouCan.Evidence/
RUN dotnet restore src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj

COPY src/BreakMeIfYouCan.Api/ src/BreakMeIfYouCan.Api/
COPY src/BreakMeIfYouCan.Evidence/ src/BreakMeIfYouCan.Evidence/
COPY --from=web /web/dist/ src/BreakMeIfYouCan.Api/wwwroot/

RUN dotnet publish src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj \
    -c Release -o /app -p:CommitSha=$COMMIT_SHA --no-restore

# ---------------------------------------------------------------- runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

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
