FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GarminAchievements.csproj .
RUN dotnet restore GarminAchievements.csproj
COPY . .
RUN dotnet publish GarminAchievements.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Fly.io/Render set PORT at runtime; Program.cs binds to it directly.
ENTRYPOINT ["dotnet", "GarminAchievements.dll"]
