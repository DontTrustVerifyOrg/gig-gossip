# %%
import hashlib
import pickle
import random
import sys
import time
import uuid
from datetime import datetime, timedelta
from functools import partial

import numpy as np
import simpy
from tqdm.auto import tqdm

import crypto
from mass import Agent, execute_simulation, simulate, trace
from mass_tools import time_to_int
from run_tools import is_notebook
from stopwatch import Stopwatch
from datetime import datetime

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

    def __repr__(self):
        return f"""AskForFavourFrame(
        topic={self.topic},
        buf_size={self.buf_size})"""

    def __str__(self):
        return self.__repr__()

# %%


class FavourConditionsFrame:
    def __init__(self, topic: str, buf_size: int, valid_till: datetime):
        self.topic = topic
        self.buf_size = buf_size
        self.valid_till = valid_till

    def __repr__(self):
        return f"""FavourConditionsFrame(
        topic={self.topic},
        buf_size={self.buf_size},
        valid_till={self.valid_till})"""

    def __str__(self):
        return self.__repr__()


class POWFavourConditionsFrame(FavourConditionsFrame):
    def __init__(self, topic: str, buf_size: int, valid_till: datetime, pow_scheme: str, pow_target: int):
        super().__init__(topic, buf_size, valid_till)
        self.pow_scheme = pow_scheme
        self.pow_target = pow_target

    def __repr__(self):
        return f"""{super().__repr__()}
        <-
        POWFavourConditionsFrame(
        pow_scheme={self.pow_scheme},
        pow_target={self.pow_target})"""


class LNFavourConditionsFrame(FavourConditionsFrame):
    def __init__(self, topic: str, buf_size: int, valid_till: datetime, ln_addr, satoshis: int):
        super().__init__(topic, buf_size, valid_till)
        self.ln_addr = ln_addr
        self.satoshis = satoshis

    def __repr__(self):
        return f"""{super().__repr__()}
        <-
        LNFavourConditionsFrame(
        ln_addr={self.ln_addr},
        satoshis={self.satoshis})"""


# %%

class OnionLayer:
    def __init__(self, peer_name: str, reward_ln_addr, reward_satoshis: int):
        self.peer_name = peer_name
        self.reward_ln_addr = reward_ln_addr
        self.reward_satoshis = reward_satoshis

    def __repr__(self):
        return f"""OnionLayer(
            peer_name={self.peer_name},
            reward_ln_addr={self.reward_ln_addr},
            reward_satoshis={self.reward_satoshis})"""

    def __str__(self):
        return self.__repr__()


class OnionRoute:
    def __init__(self):
        self._onion = b""

    def peel(self, priv_key: bytes) -> OnionLayer:
        layer, rest = crypto.decrypt_object(self._onion, priv_key)
        self._onion = rest
        return layer

    def grow(self, layer: OnionLayer, pub_key: bytes):
        self._onion = crypto.encrypt_object((layer, self._onion), pub_key)

    def buf_size(self):
        return len(self._onion)

    def clone(self):
        onion = OnionRoute()
        onion._onion = self._onion
        return onion

    def __repr__(self):
        return f"""OnionRoute({self._onion})"""

    def __str__(self):
        return self.__repr__()

# %%


class Payload:
    def __init__(self):
        self._buf = None

    def encrypt(self, message, pub_key: bytes) -> bytes:
        self._buf = crypto.encrypt_object(message, pub_key)

    def decrypt(self, priv_key: bytes):
        return crypto.decrypt_object(self._buf, priv_key)

    def __repr__(self):
        return f"""Payload({self._buf})"""

    def __str__(self):
        return self.__repr__()

# %%


class MessageFrame:
    def __init__(self, topic: str,
                 message: str,
                 thank_you_secret: bytes,
                 reply_pubkey: bytes,
                 ):
        self.topic = topic
        self.message = message
        self.thank_you_secret = thank_you_secret
        self.reply_pubkey = reply_pubkey

    def __repr__(self):
        return f"""MessageFrame(
        topic={self.topic},
        message={self.message},
        thank_you_secret={self.thank_you_secret},
        reply_pubkey={self.reply_pubkey},
        )"""

    def __str__(self):
        return self.__repr__()

    def same_as(self, other):
        return ((self.topic == other.topic)
                and (self.message == other.message)
                and (self.thank_you_secret == other.thank_you_secret)
                and (self.reply_pubkey == other.reply_pubkey)
                )

    def clone(self):
        frame = MessageFrame()
        frame.topic = self.topic
        frame.message = self.message
        frame.thank_you_secret = self.thank_you_secret
        frame.reply_pubkey = self.reply_pubkey
        return frame

    def size(self):
        return (len(self.topic)
                + len(self.message)
                + len(self.thank_you_secret)
                + len(self.reply_pubkey)
                )


class BroadcastFrame:
    def __init__(self, message_frame: MessageFrame,
                 backward_onion: OnionRoute,
                 ):
        self.message_frame = message_frame
        self.backward_onion = backward_onion

    def __repr__(self):
        return f"""BroadcastFrame(
        message_frame={self.message_frame},
        backward_onion={self.backward_onion}
        )"""

    def __str__(self):
        return self.__repr__()

# %%


class POWBroadcastFrame(BroadcastFrame):
    def __init__(self, message_frame: MessageFrame,
                 backward_onion: OnionRoute
                 ):
        super().__init__(message_frame,
                         backward_onion)

    def compute_pow(self, pow_scheme: str, pow_target: int):
        if pow_scheme.lower() == "sha256":
            buf = bytearray(pickle.dumps(self.message_frame))
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
            buf = bytearray(pickle.dumps(self.message_frame))
            m = hashlib.sha256()
            m.update(buf)
            m.update(self.nuance.to_bytes(4, "big"))
            d = int.from_bytes(m.digest(), "big")
            if d <= pow_target:
                return True
        return False

    def __repr__(self):
        return f"""{super().__repr__()}
        <-
        POWBroadcastFrame(nuance={self.nuance})"""

    def __str__(self):
        return self.__repr__()


class LNBroadcastFrame(BroadcastFrame):
    def __init__(self, message_frame: MessageFrame,
                 backward_onion: OnionRoute,
                 ln_utxo,
                 ):
        super().__init__(message_frame,
                         backward_onion)
        self.ln_utxo = ln_utxo

    def __repr__(self):
        return f"""{super().__repr__()}
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

    def __repr__(self):
        return f"""CommunicateFrame(
        payload={self.payload},
        forward_onion={self.forward_onion},
        backward_onion={self.backward_onion}
        )"""

    def __str__(self):
        return self.__repr__()

# %%


class ThankYouFrame:
    def __init__(self, thank_you_key: bytes,
                 forward_onion: OnionRoute,
                 ):
        self.thank_you_key = thank_you_key
        self.forward_onion = forward_onion

    def __repr__(self):
        return f"""ThankYouFrame(
        thank_you_key={self.thank_you_key},
        forward_onion={self.forward_onion}
        )"""

    def __str__(self):
        return self.__repr__()

# %%


class SweetGossipNode(Agent):
    def __init__(self, context_name, name, ln_addr):
        super().__init__(context_name, name, ln_addr)
        self.name = name
        self.history = []
        self._log_i = 0
        self._known_hosts = dict()
        self._asks_for = dict()
        self._priv_key, self.pub_key = crypto.create_keys()
        self._already_seen = []
        self._ln_addr = ln_addr

    def log_history(self, e, what, d={}):
        self.ctx(e, lambda: self.trace(e, str(d)))
        self.history.append(d)
        self._log_i += 1

    def connect_to(self, other):
        self._known_hosts[other.name] = other
        other._known_hosts[self.name] = self

    def broadcast(self, e,
                  topic: str,
                  message: str,
                  thank_you_secret: bytes,
                  reply_pubkey: bytes,
                  reward_satoshis: int,
                  backward_onion: OnionRoute = OnionRoute()):

        message_frame = MessageFrame(
            topic, message, thank_you_secret, reply_pubkey)
        aff_frame = AskForFavourFrame(
            topic, message_frame.size())
        self._already_seen.append(message_frame.clone())
        for peer in self._known_hosts.values():
            if not peer.name in self._asks_for:
                self._asks_for[peer.name] = []
            layer = OnionLayer(self.name, self._ln_addr, reward_satoshis)
            backward_onion.enclose(layer, self._priv_key)
            brd = POWBroadcastFrame(message_frame,
                                    backward_onion)
            self._asks_for[peer.name].append(brd)
            self.new_message(e, peer, aff_frame)

    def communicate(self, e, payload: Payload, reward_satoshis: int, forward_onion: OnionRoute, backward_onion: OnionRoute):
        top_layer = forward_onion.peel()
        if top_layer.name == self.name:
            self.on_communicate(payload.decrypt(), backward_onion)
        elif top_layer.name in self._known_hosts:
            layer = OnionLayer(self.name, self._ln_addr, reward_satoshis)
            backward_onion.enclose(layer, self._priv_key)
            com_frame = CommunicateFrame(
                payload, forward_onion, backward_onion)
            self.new_message(e, self._known_hosts[top_layer.name], com_frame)

    def respond(self, e, message: str, reward_satoshis: int, forward_onion: OnionRoute):
        payload = Payload()
        payload.encrypt(message, pub_key=self.pub_key)
        self.communicate(e, payload, reward_satoshis,
                         forward_onion=forward_onion,
                         backward_onion=OnionRoute())

    def thankyou(self, e, secret_key: str, forward_onion: OnionRoute):
        top_layer = forward_onion.peel()
        if top_layer.name != self.name:
            if top_layer.name in self._known_hosts:
                thx_frame = ThankYouFrame(secret_key, forward_onion)
                self.new_message(
                    e, self._known_hosts[top_layer.name], thx_frame)
        self.on_thankyou(secret_key)

    def on_message(self, e, m):
        if isinstance(m.data, AskForFavourFrame):
            aff_frame = m.data
            peer_name = m.sender.name
            fc_cond = POWFavourConditionsFrame(
                aff_frame.topic, aff_frame.buf_size,
                valid_till=datetime.now()+timedelta(minutes=10),
                pow_scheme="sha256",
                pow_target=MAX_POW_TARGET_SHA256)
            self.reply(e, m, fc_cond)
        elif isinstance(m.data, POWFavourConditionsFrame):
            fc_cond = m.data
            if fc_cond.valid_till <= datetime.now():
                for i in range(len(self._asks_for[peer_name])):
                    if self._asks_for[peer_name].peek(i).message_frame.size() == fc_cond.buf_size:
                        brd_frame = self._asks_for[peer_name].pop(i)
                        break
                if isinstance(brd_frame, POWBroadcastFrame):
                    brd_frame.compute_pow(
                        fc_cond.pow_scheme,
                        fc_cond.pow_target)
                    self.reply(e, m, brd_frame)
                elif isinstance(brd_frame, LNBroadcastFrame):
                    pass
        elif isinstance(m.data, BroadcastFrame):
            brd_frame = m.data
            payload = self.accept_broadcast(brd_frame)
            if payload is not None:
                self.communicate(e, payload=payload,
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
