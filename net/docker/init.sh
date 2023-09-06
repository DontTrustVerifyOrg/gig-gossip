

# clean old data
rm -rf ./work/

# create workspace structure
mkdir ./work
mkdir ./work/locallnd

cp -r ./config/. ./work/locallnd/.

# docker run -it awazcognitum/gig-gossip-base:0.1 /work/bitcoin/share/rpcauth/rpcauth.py lnd lightning
# docker exec -it giggossip_bitcoin /work/bitcoin/share/rpcauth/rpcauth.py lnd lightning

