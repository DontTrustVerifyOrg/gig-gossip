FROM awazcognitum/gig-gossip-base:0.1

WORKDIR /work/gig-gossip-build
COPY ./NGigGossip4Nostr/ ./NGigGossip4Nostr/

WORKDIR /work/gig-gossip-build/NGigGossip4Nostr/GigLNDWalletAPI
RUN dotnet build 

WORKDIR /work/gig-gossip-build/NGigGossip4Nostr/GigGossipSettlerAPI
RUN dotnet build 


WORKDIR /work/gig-gossip-build/
COPY ./docker/docker_services_build/giggossip_entrypoint.sh ./giggossip_entrypoint.sh

ENTRYPOINT ./giggossip_entrypoint.sh