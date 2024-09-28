# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY ./NGigGossip4Nostr/ .
RUN dotnet restore
RUN dotnet publish -c Release -o out ./GigGossipSettlerAPI/GigGossipSettlerAPI.csproj

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt update && apt install -y gettext jq curl
WORKDIR /app
RUN mkdir -p /app/data/
COPY ./docker/backend/entrypoint.sh .
COPY --from=build /app/out .
COPY ./docker/backend/settler.conf.template /app/settler.conf.template

ENV ListenHost=http://0.0.0.0:80/
EXPOSE 80
ENTRYPOINT ["./entrypoint.sh", "GigGossipSettlerAPI.dll", "settler.conf"]