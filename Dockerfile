# syntax=docker/dockerfile:1
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
# DevExpress license for the build. For v25.1+ packages restore from nuget.org, but the build
# still needs the license key injected (exact casing "DevExpress_License").
# Pass it: docker build --build-arg DevExpress_License=<YOUR_KEY> ...
ARG DevExpress_License
ENV DevExpress_License=${DevExpress_License}
# Release omits the dev Admin/User seeding (#if !RELEASE). Use Debug for an evaluation image
# that seeds default users. Build: --build-arg BUILD_CONFIGURATION=Debug
ARG BUILD_CONFIGURATION=Release

WORKDIR /src
COPY . .
RUN dotnet restore xaf/DataspaceOperator.Xaf.Blazor.Server/DataspaceOperator.Xaf.Blazor.Server.csproj
RUN dotnet publish xaf/DataspaceOperator.Xaf.Blazor.Server/DataspaceOperator.Xaf.Blazor.Server.csproj \
    -c ${BUILD_CONFIGURATION} -o /app/publish --no-restore -p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p /app/data
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
# Default entrypoint serves the app. The Helm chart runs "--updateDatabase" in an init container.
ENTRYPOINT ["dotnet", "DataspaceOperator.Xaf.Blazor.Server.dll"]
