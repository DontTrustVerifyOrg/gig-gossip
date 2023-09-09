Running entire setup locally
=====


Initialization and first start
-----

> ***Important note:*** During initailization process you will be asked about **password**. Use `testertester` password to make sure that all services will be working properly.

1. Initialize local setup and starts the stack (sometimes sudo is required in order to clean previously generated data):

    ```bash
    $ ./re_init.sh
    ```

2. Run basic tests to make sure system runs smothly
    1. Source *bash_aliases*

        ```bash
        $ source ./bash_aliases
        ```

    2. Bitcoin network test

        ```bash
        $ btc-test
        ```


CLI scripts
-----

1. Source commands (only needed for configuration purposes when using cli)

    ```bash
    $ source ./bash_aliases
    ```

2. Start using bitcoin local network or lightning network as described in <https://github.com/DontTrustVerifyOrg/gig-gossip/tree/main/net/NGigGossip4Nostr>

    ```bash
    $ bitcoin-local-cli
    $ lnd1
    $ lnd2
    $ lnd3
    $ lncli1
    $ lncli2
    $ lncli3
    $ btc-test
    $ lnd-test
    ```

Standard usage
-----

- Run all services using docker compose:

    ```bash
    $ docker compose up -d
    ```

- Stop all services

    ```bash
    $ docker compose down
    ```

- Check logs

    ```bash
    $ docker compose logs -f
    ```


