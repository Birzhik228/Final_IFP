FROM mcr.microsoft.com/dotnet/sdk:8.0.428 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ ./src/
RUN dotnet restore src/LibraryLending.Web/LibraryLending.Web.csproj --locked-mode
RUN dotnet publish src/LibraryLending.Web/LibraryLending.Web.csproj \
    --configuration Release --no-restore --output /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000 \
    DOTNET_EnableDiagnostics=0
EXPOSE 10000
RUN mkdir -p /tmp/bookbridge/keys \
    && chown -R app:app /tmp/bookbridge \
    && chmod -R 700 /tmp/bookbridge
COPY --from=build /publish/ ./
USER app
ENTRYPOINT ["dotnet", "LibraryLending.Web.dll"]
