FROM awazcognitum/gig-gossip-base:0.1

WORKDIR /work/gig-gossip/net/NGigGossip4Nostr/GigLNDWalletAPI
RUN dotnet build 

WORKDIR /work/gig-gossip/net/NGigGossip4Nostr/GigGossipSettlerAPI
RUN dotnet build 


WORKDIR /work/gig-gossip/net
COPY ./docker/docker_services_build/giggossip_entrypoint.sh ./giggossip_entrypoint.sh


WORKDIR /work/locallnd/

ENTRYPOINT /work/gig-gossip/net/giggossip_entrypoint.sh