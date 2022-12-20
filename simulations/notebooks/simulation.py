# %%
import hashlib
import pickle
import random
import sys
import time
import uuid
from datetime import datetime
from functools import partial

import numpy as np
import simpy
from tqdm.auto import tqdm

import crypto
from mass import Agent, execute_simulation, simulate, trace
from mass_tools import time_to_int
from run_tools import is_notebook
from stopwatch import Stopwatch

if is_notebook():
    PARAM_ID = 295
else:
    PARAM_ID = int(sys.argv[-1])

RANDOM_SEED = 1234

random.seed(RANDOM_SEED)
np.random.seed(RANDOM_SEED)

FOLDNAME = f"sim/{PARAM_ID:08d}_"+(str(datetime.now())+" "+uuid.uuid4().hex).replace('-',
                                                                                     '').replace(' ', '_').replace(':', '').replace('.', '_')
RUN_START = time_to_int(1, 8, 0)

# %%
MAX_POW_TARGET_SHA256 = int.from_bytes(
    b'\x0F\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF', 'big')

# %%


class AskForFavourFrame:
    def __init__(self, topic: str, buf_size: int):
        self.topic = topic
        self.buf_size = buf_size

    def __repl__(self):
        return f"""AskForFavourFrame(
        topic={self.topic},
        buf_size={self.buf_size})"""

    def __str__(self):
        return self.__repl__()

# %%


class FavourConditionsFrame:
    def __init__(self, topic: str, favour: int, timestamp):
        self.topic = topic
        self.favour = favour
        self.timestamp = timestamp

    def __repl__(self):
        return f"""FavourConditionsFrame(
        topic={self.topic},
        favour={self.favour},
        timestamp={self.timestamp})"""

    def __str__(self):
        return self.__repl__()


class POWFavourConditionsFrame(FavourConditionsFrame):
    def __init__(self, topic: str, favour: int, timestamp, pow_scheme: str, pow_target: int):
        super().__init__(topic, favour, timestamp)
        self.pow_scheme = pow_scheme
        self.pow_target = pow_target

    def __repl__(self):
        return f"""{super().__repl__()}
        <-
        POWFavourConditionsFrame(
        pow_scheme={self.pow_scheme},
        pow_target={self.pow_target})"""


class LNFavourConditionsFrame(FavourConditionsFrame):
    def __init__(self, topic: str, favour: int, timestamp, pow_scheme: str, ln_addr, satoshis: int):
        super().__init__(topic, favour, timestamp)
        self.ln_addr = ln_addr
        self.satoshis = satoshis

    def __repl__(self):
        return f"""{super().__repl__()}
        <-
        LNFavourConditionsFrame(
        ln_addr={self.ln_addr},
        satoshis={self.satoshis})"""


# %%


class OnionRoute:
    def __init__(self):
        self._onion = b""

    def peel(self, priv_key: bytes):
        layer, rest = crypto.decrypt_object(self._onion, priv_key)
        self._onion = rest
        return layer

    def grow(self, peer_name, pub_key: bytes):
        self._onion = crypto.encrypt_object((peer_name, self._onion), pub_key)

    def buf_size(self):
        return len(self._onion)

    def clone(self):
        onion = OnionRoute()
        onion._onion = self._onion
        return onion

    def __repl__(self):
        return f"""OnionRoute({self._onion})"""

    def __str__(self):
        return self.__repl__()

# %%


class Payload:
    def __init__(self):
        self._buf = None

    def encrypt(self, message, pub_key: bytes) -> bytes:
        self._buf = crypto.encrypt_object(message, pub_key)

    def decrypt(self, priv_key: bytes):
        return crypto.decrypt_object(self._buf, priv_key)

    def __repl__(self):
        return f"""Payload({self._buf})"""

    def __str__(self):
        return self.__repl__()

# %%


class BroadcastFrame:
    def __init__(self, topic: str, favour: int, timestamp,
                 message,
                 thank_you_secret: bytes,
                 reply_pubkey: bytes,
                 reply_ln_addr,
                 reply_price: int,
                 backward_onion: OnionRoute,
                 ):
        self.topic = topic
        self.favour = favour
        self.timestamp = timestamp
        self.message = message
        self.thank_you_secret = thank_you_secret
        self.reply_pubkey = reply_pubkey
        self.reply_ln_addr = reply_ln_addr
        self.reply_price = reply_price
        self.backward_onion = backward_onion

    def __repl__(self):
        return f"""BroadcastFrame(
        topic={self.topic},
        favour={self.favour},
        timestamp={self.timestamp},
        message={self.message},
        thank_you_secret_pubkey={self.thank_you_secret},
        reply_pubkey={self.reply_pubkey},
        reply_ln_addr={self.reply_ln_addr},
        reply_price={self.reply_price}
        backward_onion={self.backward_onion}
        )"""

    def __str__(self):
        return self.__repl__()


    def is_same_as(self, other):
        return ((self.topic == other.topic)
                and (self.favour == other.favour)
                and (self.timestamp == other.timestamp)
                and (self.message == other.message)
                and (self.thank_you_secret == other.thank_you_secret)
                and (self.reply_pubkey == other.reply_pubkey)
                )

    def clone_for_check(self):
        frame = BroadcastFrame()
        frame.topic = self.topic
        frame.favour = self.favour
        frame.timestamp = self.timestamp
        frame.message = self.message
        frame.thank_you_secret = self.thank_you_secret
        frame.reply_pubkey = self.reply_pubkey
        return frame
# %%


class POWBroadcastFrame(BroadcastFrame):
    def __init__(self,
                 topic: str, favour: int, timestamp,
                 message,
                 thank_you_secret: bytes,
                 reply_pubkey: bytes,
                 reply_ln_addr,
                 reply_price: int,
                 backward_onion: OnionRoute,
                 ):
        super().__init__(topic, favour, timestamp, message,
                         thank_you_secret,
                         reply_pubkey, reply_ln_addr, reply_price, backward_onion)


    def compute_pow(self, pow_scheme: str, pow_target: int):
        if pow_scheme.lower() == "sha256":
            self.nuance = None
            buf = bytearray(pickle.dumps(self))
            for n in range(sys.maxsize):
                m = hashlib.sha256()
                m.update(buf)
                m.update(n.to_bytes(4, "big"))
                d = int.from_bytes(m.digest(), "big")
                if d <= pow_target:
                    self.nuance = n
                    return

    def validate_pow(self, pow_scheme: str, pow_target: int):
        if pow_scheme.lower() == "sha256":
            n = self.nuance
            self.nuance = None
            buf = bytearray(pickle.dumps(self))
            self.nuance = n
            m = hashlib.sha256()
            m.update(buf)
            m.update(n.to_bytes(4, "big"))
            d = int.from_bytes(m.digest(), "big")
            if d <= pow_target:
                return True
        return False

    def __repl__(self):
        return f"""{super().__repl__()}
        <-
        POWBroadcastFrame(pow_nuance={self.pow_nuance})"""

    def __str__(self):
        return self.__repl__()


class LNBroadcastFrame(BroadcastFrame):
    def __init__(self, topic: str, favour: int, timestamp,
                 ln_utxo,
                 message,
                 thank_you_secret: bytes,
                 reply_pubkey: bytes,
                 reply_ln_addr,
                 reply_price: int,
                 backward_onion: OnionRoute
                 ):
        super().__init__(topic, favour, timestamp, message,
                         thank_you_secret,
                         reply_pubkey, reply_ln_addr, reply_price, backward_onion)
        self.ln_utxo = ln_utxo

    def __repl__(self):
        return f"""{super().__repl__()}
        <-
        LNBroadcastFrame(ln_utxo={self.ln_utxo})
        """


# %%
class CommunicateFrame:
    def __init__(self, payload: Payload,
                 forward_onion: OnionRoute,
                 backward_onion: OnionRoute,
                 ):
        self.payload = payload
        self.forward_onion = forward_onion
        self.backward_onion = backward_onion

    def __repl__(self):
        return f"""CommunicateFrame(
        payload={self.payload},
        forward_onion={self.forward_onion},
        backward_onion={self.backward_onion}
        )"""

    def __str__(self):
        return self.__repl__()

# %%


class ThankYouFrame:
    def __init__(self, thank_you_key: bytes,
                 forward_onion: OnionRoute,
                 ):
        self.thank_you_key = thank_you_key
        self.forward_onion = forward_onion

    def __repl__(self):
        return f"""ThankYouFrame(
        thank_you_key={self.thank_you_key},
        forward_onion={self.forward_onion}
        )"""

    def __str__(self):
        return self.__repl__()

# %%


class SweetGossipNode(Agent):
    def __init__(self, context_name, name):
        super().__init__(context_name, name)
        self.name = name
        self.history = []
        self._log_i = 0
        self._known_hosts = dict()
        self._asks_for = dict()
        self._priv_key, self.pub_key = crypto.create_keys()
        self._already_seen = []

    def log_history(self, e, what, d={}):
        self.ctx(e, lambda: self.trace(e, str(d)))
        self.history.append(d)
        self._log_i += 1

    def connect_to(self, other):
        self._known_hosts[other.name] = other
        other._known_hosts[self.name] = self

    def calc_buf_size(self, topic: str, message: str, onion: OnionRoute):
        return len(topic)+len(message)+onion.buf_size()

    def broadcast(self, e, topic: str, message: str, backward_onion: OnionRoute = OnionRoute()):
        aff_frame = AskForFavourFrame(
            topic, self.calc_buf_size(topic, message, backward_onion))
        for peer in self._known_hosts.values():
            if not peer.name in self._asks_for:
                self._asks_for[peer.name] = []
            backward_onion.enclose(self.name, self._priv_key)
            brd = POWBroadcastFrame(topic,
                                    0, 0, 0,
                                    message,
                                    "ts1",
                                    "rpub",
                                    "rln",
                                    1,
                                    backward_onion)
            self._asks_for[peer.name].append(brd)
            self.new_message(e, peer, aff_frame)

    def communicate(self, e, payload: Payload, forward_onion: OnionRoute, backward_onion: OnionRoute):
        host_name = forward_onion.peel()
        if host_name == self.name:
            self.on_communicate(payload.decrypt(), backward_onion)
        elif host_name in self._known_hosts:
            backward_onion.enclose(self.name, self._priv_key)
            com_frame = CommunicateFrame(
                payload, forward_onion, backward_onion)
            self.new_message(e, self._known_hosts[host_name], com_frame)

    def respond(self, e, message: str, forward_onion: OnionRoute):
        payload = Payload()
        payload.encrypt(message, pub_key=self.pub_key)
        self.communicate(e, payload, forward_onion=forward_onion,
                         backward_onion=OnionRoute())

    def thankyou(self, e, secret_key: str, forward_onion: OnionRoute):
        host_name = forward_onion.peel()
        if host_name != self.name:
            if host_name in self._known_hosts:
                thx_frame = ThankYouFrame(secret_key, forward_onion)
                self.new_message(e, self._known_hosts[host_name], thx_frame)
        self.on_thankyou(secret_key)

    def on_message(self, e, m):
        if isinstance(m.data, AskForFavourFrame):
            aff_frame = m.data
            peer_name = m.sender.name
            fcond = POWFavourConditionsFrame(
                aff_frame.topic, 0, 0, "SHA256", 1)
            self.reply(e, m, fcond)
        elif isinstance(m.data, FavourConditionsFrame):
            fc_frame = m.data
            brd_frame = self._asks_for[peer_name].pop(0)
            if isinstance(brd_frame, POWBroadcastFrame):
                brd_frame.nuance = 0
                self.reply(e, m, brd_frame)
            elif isinstance(brd_frame, LNBroadcastFrame):
                pass
        elif isinstance(m.data, BroadcastFrame):
            brd_frame = m.data
            payload = self.accept_broadcast(brd_frame)
            if payload is not None:
                self.comunicate(e, payload=payload,
                                forward_onion=brd_frame.backward_onion)
            else:
                self.broadcast(e, topic=brd_frame.topic, message=brd_frame.message,
                               backward_onion=brd_frame.backward_onion)
        elif isinstance(m.data, CommunicateFrame):
            com_frame = m.data
            self.communicate(e, payload=com_frame.payload,
                             onion=com_frame.onion)
        elif isinstance(m.data, ThankYouFrame):
            thx_frame = m.data
            self.thankyou(e, secret_key=thx_frame.secret_key,
                          onion=thx_frame.onion)
        else:
            self.ctx(e, lambda: self.trace(e, "unknown request:", m))

    def accept_broadcast(self, brd_frame: BroadcastFrame) -> Payload:
        return None

    def on_communicate(self, message: str, forward_onion: OnionRoute):
        pass

    def on_thankyou(self, secret_key: str):
        pass


# %%
class Gossiper(SweetGossipNode):
    pass

# %%


class GigWorker(SweetGossipNode):
    pass

# %%


class Customer(SweetGossipNode):

    def homeostasis(self, e):
        self.ctx(e, lambda: self.trace(e, "is starting..."))

        self.schedule(partial(self.run_job, {"run"}),
                      partial(self.on_return, e), RUN_START)

        yield simpy.events.AllOf(e, [e.process(self.run_scheduler(e))])

        yield e.timeout(float('inf'))

    def run_job(self, what, e):
        def processor():
            if False:
                yield e.timeout(0)
            self.broadcast(e, "test topic", "test message")
            return None,

        self.ctx(e, lambda: self.trace(e, what))
        self.log_history(e, "run_job", what)
        return e.process(processor())

    def on_return(self, e, val):
        self.ctx(e, lambda: self.trace(e, val))

# %%


# %%
def main(sim_id):
    start = time.time()

    def printMessages(msgs):
        for m in msgs:
            print(m)

    things = {}

    things["Gossiper1"] = Gossiper("Gossipers", "Gossiper1")
    things["GigWorker1"] = GigWorker("GigWorkers", "GigWorker1")
    things["Customer1"] = Customer("Customers", "Customer1")

    things["GigWorker1"].connect_to(things["Gossiper1"])
    things["Customer1"].connect_to(things["Gossiper1"])

    simulate(sim_id, things, verbose={
        "message flow",
        "GigWorkers",
        "Gossipers",
        "Customers",
        "unknown message"}, until=float('inf'))

    for a in things:
        if (len(things[a].queue.items) > 0):
            print(a)
            printMessages(things[a].queue.items)

    end = time.time()
    print(end - start)


# %%
execute_simulation(main, sim_id=FOLDNAME)

# %%


# %%
