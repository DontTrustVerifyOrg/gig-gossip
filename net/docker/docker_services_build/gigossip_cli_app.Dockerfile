FROM awazcognitum/gig-gossip-base:0.1

WORKDIR /work/gig-gossip-build
COPY ./NGigGossip4Nostr/ ./NGigGossip4Nostr/

WORKDIR /work/gig-gossip-build/NGigGossip4Nostr/RideShareCLIApp
RUN dotnet build 

WORKDIR /work/gig-gossip-build/
