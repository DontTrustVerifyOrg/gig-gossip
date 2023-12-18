#!/bin/bash


SERVICES_STARTUP_TIME=60


docker compose down

# clean old data
rm -rf ./work/


# source bash_aliases
BITCOIN_LOCAL_DIR="/work/locallnd/.bitcoin"
LND_DIR="/work/locallnd/.lnd"


# create workspace structure
mkdir ./work
mkdir ./work/locallnd

cp -r ./config/. ./work/locallnd/.


docker compose up -d bitcoin
printf "Starting bitcoin service...\n"
sleep $SERVICES_STARTUP_TIME

printf "Running command: bitcoin-cli createwallet testwallet\n"
docker exec -it giggossip_bitcoin /work/bitcoin/src/bitcoin-cli -datadir=$BITCOIN_LOCAL_DIR createwallet "testwallet"
printf "Running command: bitcoin-cli -generate 101\n"
docker exec -it giggossip_bitcoin /work/bitcoin/src/bitcoin-cli -datadir=$BITCOIN_LOCAL_DIR -generate 10


docker run -it -d --name giggossip_lightning_node_1 -v ./work/locallnd/.lnd:$LND_DIR:Z awazcognitum/gig-gossip-base:0.1 /work/lnd/lnd-debug --lnddir=$LND_DIR
docker run -it -d --name giggossip_lightning_node_2 -v ./work/locallnd/.lnd2:$LND_DIR:Z awazcognitum/gig-gossip-base:0.1 /work/lnd/lnd-debug --lnddir=$LND_DIR
docker run -it -d --name giggossip_lightning_node_3 -v ./work/locallnd/.lnd3:$LND_DIR:Z awazcognitum/gig-gossip-base:0.1 /work/lnd/lnd-debug --lnddir=$LND_DIR
printf "Lightning network initialization services...\n"
sleep $SERVICES_STARTUP_TIME



printf "\n\nSetup 1st Lightning network node:\n\n"
docker exec -it giggossip_lightning_node_1 /work/lnd/lncli-debug -n regtest --lnddir=$LND_DIR --rpcserver=localhost:10009 create
docker stop giggossip_lightning_node_1
docker rm giggossip_lightning_node_1

printf "\n\nSetup 2nd Lightning network node:\n\n"
docker exec -it giggossip_lightning_node_2 /work/lnd/lncli-debug -n regtest --lnddir=$LND_DIR --rpcserver=localhost:11009 create
docker stop giggossip_lightning_node_2
docker rm giggossip_lightning_node_2

printf "\n\nSetup 3rd Lightning network node:\n\n"
docker exec -it giggossip_lightning_node_3 /work/lnd/lncli-debug -n regtest --lnddir=$LND_DIR --rpcserver=localhost:11010 create
docker stop giggossip_lightning_node_3
docker rm giggossip_lightning_node_3


docker compose up -d
