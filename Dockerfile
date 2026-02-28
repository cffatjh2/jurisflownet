FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY JurisFlow.Server/JurisFlow.Server.csproj JurisFlow.Server/
RUN dotnet restore JurisFlow.Server/JurisFlow.Server.csproj

COPY JurisFlow.Server/ JurisFlow.Server/
RUN dotnet publish JurisFlow.Server/JurisFlow.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD sh -c "curl --fail http://localhost:${PORT:-8080}/health || exit 1"

ENTRYPOINT ["dotnet", "JurisFlow.Server.dll"]
