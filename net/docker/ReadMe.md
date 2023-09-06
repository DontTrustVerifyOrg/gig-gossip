Running entire setup locally
=====

1. Initialize local setup (sometimes sudo is required in order to clean previously generated data):
    `$ ./init.sh`
2. Run all services using docker compose:
    `$ docker compose up -d`
3. Source commands:
    `$ source ./bash_aliases`
4. Start using bitcoin local network or lightning network as described in https://github.com/DontTrustVerifyOrg/gig-gossip/tree/main/net/NGigGossip4Nostr
    Currently only limited amount of commands are supported:
        `$ bitcoin-local-cli`
        `$ lnd1`
        `$ lnd2`
        `$ lnd3`
        `$ lncli1`
        `$ lncli2`
        `$ lncli3`
