# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY ./NGigGossip4Nostr/ .
RUN dotnet restore
RUN dotnet publish -c Release -o out ./KeyPairGen/KeyPairGen.csproj

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN mkdir -p /app/data/
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "KeyPairGen.dll"]
