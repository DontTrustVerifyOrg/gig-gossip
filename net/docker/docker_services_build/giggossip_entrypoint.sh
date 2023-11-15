#!/bin/sh


dotnet ../gig-gossip/net/NGigGossip4Nostr/GigLNDWalletAPI/bin/Debug/net7.0/GigLNDWalletAPI.dll --basedir=/work/locallnd/.giggossip/ &
sleep 10
dotnet ../gig-gossip/net/NGigGossip4Nostr/GigGossipSettlerAPI/bin/Debug/net7.0/GigGossipSettlerAPI.dll --basedir=/work/locallnd/.giggossip/ &

sleep infinity