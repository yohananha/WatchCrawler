FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GarminAchievements.csproj .
RUN dotnet restore GarminAchievements.csproj
COPY . .
RUN dotnet publish GarminAchievements.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Shown in error reports, so a report can be matched to the code that produced it (CI passes the git hash).
ARG BUILD_STAMP=dev
ENV BUILD_STAMP=$BUILD_STAMP
# State (install id, usage, report counters, joke history) lives here; mount a volume on it.
ENV DATA_DIR=/data
ENV HISTORY_STATE_PATH=/data/history.json
ENV TRIGGER_STATE_PATH=/data/trigger.flag
# Fly.io/Render set PORT at runtime; Program.cs binds to it directly.
ENTRYPOINT ["dotnet", "GarminAchievements.dll"]
