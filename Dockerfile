# syntax=docker/dockerfile:1
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
# Release omits the dev Admin/User seeding (#if !RELEASE). Use Debug for an evaluation image
# that seeds default users. Build: --build-arg BUILD_CONFIGURATION=Debug
ARG BUILD_CONFIGURATION=Release

# DevExpress license as a FILE (the build-arg/env-var route is NOT honoured by the DevExpress
# build tooling and yields watermarked evaluation builds). On Linux the license is read from
# $HOME/.config/DevExpress/DevExpress_License.txt. The CI workflow writes the DEVEXPRESS_LICENSE
# secret into DevExpress_License.txt in the build context (never committed, .gitignore'd, and
# only present in this build stage — it is not copied into the runtime image below).
USER root
RUN mkdir -p /root/.config/DevExpress
COPY DevExpress_License.txt /root/.config/DevExpress/DevExpress_License.txt
RUN echo "DevExpress license file: $(wc -c < /root/.config/DevExpress/DevExpress_License.txt) bytes"

WORKDIR /src
COPY . .
RUN dotnet restore xaf/DataspaceOperator.Xaf.Blazor.Server/DataspaceOperator.Xaf.Blazor.Server.csproj
RUN dotnet publish xaf/DataspaceOperator.Xaf.Blazor.Server/DataspaceOperator.Xaf.Blazor.Server.csproj \
    -c ${BUILD_CONFIGURATION} -o /app/publish --no-restore -p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
# Native deps for DevExpress.Drawing.Skia (SkiaSharp): libfontconfig + freetype.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p /app/data
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
# Default entrypoint serves the app. The Helm chart runs "--updateDatabase" in an init container.
ENTRYPOINT ["dotnet", "DataspaceOperator.Xaf.Blazor.Server.dll"]
