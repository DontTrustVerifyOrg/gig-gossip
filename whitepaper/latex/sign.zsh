gpg --detach-sig --output=gig_gossip.sig gig_gossip.pdf
sha512sum gig_gossip.sig | cut -d " " -f 1 >gig_gossip.sig.sha256