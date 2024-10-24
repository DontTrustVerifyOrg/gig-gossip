# Requiements for local runner

Minimal hardware requirements:

- 2 CPU
- 4 GB RAM
- x86_64 platform

Software:

- docker
- docker compose

# Environment setup on local machine

Setup commands are based on Linux, on Windows some of the commandline commands might need to be adjusted. Generally the services itself work the same on both platforms.

Run bitcoin node

```bash
docker compose up -d bitcoin
```

Run lightning node

```bash
docker run -d --name lightning_node --rm -v $(pwd)/data/lnd:/app_data:Z -v $(pwd)/conf/lnd/lnd.conf:/app_data/lnd.conf:ro lightninglabs/lnd:v0.18.3-beta lnd --lnddir=/app_data
```

Create lightning wallet, afterwards provide password from `conf/lnd/password.txt` when asked, then generate or provide a mnemonic according to the instructions.

```bash
docker exec -it lightning_node lncli -n regtest --lnddir=/app_data --rpcserver=localhost:11009 create
```

Stop lightning node if hasn't stopped automatically

```bash
docker stop lightning_node
```

Start lightning node from compose

```bash
docker compose up -d lightning_node
```


Create btc test wallet

```bash
docker exec -it bitcoin bitcoin-cli -datadir=/app_data createwallet "testwallet"
```


Mine 10 blocks to synchronize the Lightning Node

```bash
docker exec -it bitcoin bitcoin-cli -datadir=/app_data -generate 10
```


Run rest of the services (if needed)

```bash
docker compose up -d
```


# Run services from docker compose

```bash
docker compose up -d
```

# Stop services

```bash
docker compose down
```

# Synchronize the Lightning Node

```bash
docker exec -it bitcoin bitcoin-cli -datadir=/app_data -generate 10
```

# Bitcon node management

```bash
docker exec -it bitcoin bitcoin-cli -datadir=/app_data <command>
```

#### List of commands

```bash
docker exec -it bitcoin bitcoin-cli -datadir=/app_data help
```

#### More efficient way to interact with bitcoin management

```bash
alias bcli="docker exec -it bitcoin bitcoin-cli -datadir=/app_data"
bcli <command>
```


# Lightning node management

```bash
docker exec -it lightning_node lncli -n regtest --lnddir=/app_data --rpcserver=localhost:11009 <command>
```

#### List of commands

```bash
docker exec -it lightning_node lncli -n regtest --lnddir=/app_data --rpcserver=localhost:11009 help
```

#### More efficient way to interact with lightning node management

```bash
alias lcli="docker exec -it lightning_node lncli -n regtest --lnddir=/app_data --rpcserver=localhost:11009"
lcli <command>
```

# Mnemonic, private key, public key generator

```bash
docker run --rm -it awazcognitum/keypairgen
```
