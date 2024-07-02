FROM bitnami/dotnet-sdk:8

WORKDIR /work/gig-gossip-build
COPY ./NGigGossip4Nostr/ ./NGigGossip4Nostr/

WORKDIR /work/gig-gossip-build/NGigGossip4Nostr/GigLNDWalletAPI
RUN dotnet build 

WORKDIR /work/gig-gossip-build/NGigGossip4Nostr/GigGossipSettlerAPI
RUN dotnet build 

WORKDIR /work/gig-gossip-build/NGigGossip4Nostr/GigDebugLoggerAPI
RUN dotnet build 


WORKDIR /work/gig-gossip-build/

# COPY ./docker/docker_services_build/giggossip_entrypoint.sh ./giggossip_entrypoint.sh
# ENTRYPOINT ./giggossip_entrypoint.sh
