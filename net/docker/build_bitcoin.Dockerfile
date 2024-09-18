FROM debian:12.7

ENV VERSION=27.1

RUN apt update && apt install -y curl

WORKDIR /app
RUN curl -O https://bitcoincore.org/bin/bitcoin-core-${VERSION}/bitcoin-${VERSION}-x86_64-linux-gnu.tar.gz
RUN tar -xvf bitcoin-${VERSION}-x86_64-linux-gnu.tar.gz
RUN mv ./bitcoin-${VERSION}/* .
RUN rmdir ./bitcoin-${VERSION} && bitcoin-${VERSION}-x86_64-linux-gnu.tar.gz

WORKDIR /app/bin

