FROM debian:12.7 AS build

ENV VERSION=27.1

RUN apt update && apt install -y curl

WORKDIR /app
RUN curl -O https://bitcoincore.org/bin/bitcoin-core-${VERSION}/bitcoin-${VERSION}-x86_64-linux-gnu.tar.gz
RUN tar -xvf bitcoin-${VERSION}-x86_64-linux-gnu.tar.gz
RUN mv ./bitcoin-${VERSION}/* .


FROM debian:12.7

RUN apt update && apt install -y gettext jq curl

WORKDIR /app
ENV PATH /app:${PATH}
RUN echo "${PATH}" >> /etc/bash.bashrc

COPY --from=build /app/bin .
COPY ./docker/btc/bitcoin.conf.template /app/bitcoin.conf.template
COPY ./docker/btc/entrypoint.sh /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]