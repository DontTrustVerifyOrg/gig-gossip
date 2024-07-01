FROM ubuntu:22.04

RUN apt update
RUN apt install -y build-essential libtool autotools-dev automake pkg-config bsdmainutils python3 libevent-dev libboost-dev libsqlite3-dev libminiupnpc-dev libnatpmp-dev libzmq3-dev systemtap-sdt-dev dotnet-sdk-8.0 nano wget git inetutils-ping

# install go (in apt only 1.18 version is availabe, >1.19 is required)
WORKDIR /root/tmp/
RUN wget  https://go.dev/dl/go1.20.2.linux-amd64.tar.gz
RUN tar -xvf go1.20.2.linux-amd64.tar.gz
RUN mv go /usr/local
ARG GOROOT=/usr/local/go
ARG PATH=$GOPATH/bin:$GOROOT/bin:$PATH

# get giggossip, bitcoin and lightning network
WORKDIR /work
RUN git clone https://github.com/DontTrustVerifyOrg/gig-gossip.git
RUN git clone https://github.com/bitcoin/bitcoin.git
RUN git clone https://github.com/lightningnetwork/lnd.git

# compile bitcoin
WORKDIR /work/bitcoin
RUN ./autogen.sh
RUN ./configure --without-gui
RUN make
RUN make install

# compile ligtning network
WORKDIR /work/lnd
RUN make tags="signrpc walletrpc routerrpc invoicesrpc chainrpc"
RUN make install

#compile gig-gossip examples
WORKDIR /work/gig-gossip/net/NGigGossip4Nostr/BTCTest
RUN dotnet build
WORKDIR /work/gig-gossip/net/NGigGossip4Nostr/LNDTest
RUN dotnet build


WORKDIR /work

# CMD ["sleep","infinity"]
