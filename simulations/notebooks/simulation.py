# %%
from datetime import datetime, timedelta
from functools import partial
from typing import List

import simpy
from tqdm.auto import tqdm

import crypto
from mass import Agent, execute_simulation, simulate, trace
from mass_tools import time_to_int
from stopwatch import Stopwatch
from datetime import datetime

from cert import Certificate, CertificationAuthority, verify_certificate
from payments import PaymentChannel, Invoice, ProofOfPayment, is_invoice_paid, validate_proof_of_payment
from pow import WorkRequest, ProofOfWork

from experiment_tools import FOLDNAME, RUN_START
from myrepr import ReprObject
# %%

from uuid import UUID


class AskForBroadcastFrame(ReprObject):
    def __init__(self, certificate: Certificate, topic: str, buf_size: int, ask_id: UUID) -> None:
        self.certificate = certificate
        self.topic = topic
        self.buf_size = buf_size


class BroadcastConditionsFrame(ReprObject):
    def __init__(self, ask_id: UUID, valid_till: datetime) -> None:
        self.ask_id = ask_id
        self.valid_till = valid_till


class POWBroadcastConditionsFrame(BroadcastConditionsFrame):
    def __init__(self, ask_id: UUID, valid_till: datetime, work_request: WorkRequest) -> None:
        super().__init__(ask_id, valid_till)
        self.work_request = work_request


class InvoicedBroadcastConditionsFrame(BroadcastConditionsFrame):
    def __init__(self, ask_id: UUID, valid_till: datetime, invoice: Invoice) -> None:
        super().__init__(ask_id, valid_till)
        self.invoice = invoice

# %%


class OnionLayer(ReprObject):
    def __init__(self, peer_name: str):
        self.peer_name = peer_name


class POWOnionLayer(OnionLayer):
    def __init__(self, peer_name: str, work_request: WorkRequest):
        super().__init__(peer_name)
        self.work_request = WorkRequest


class InvoicedOnionLayer(OnionLayer):
    def __init__(self, peer_name: str, invoice: Invoice):
        super().__init__(peer_name)
        self.invoice = invoice


class OnionRoute(ReprObject):
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


# %%


class Payload(ReprObject):
    def __init__(self):
        self._buf = None

    def encrypt(self, message, pub_key: bytes) -> bytes:
        self._buf = crypto.encrypt_object(message, pub_key)

    def decrypt(self, priv_key: bytes):
        return crypto.decrypt_object(self._buf, priv_key)

# %%


class MessageFrame(ReprObject):
    def __init__(self, topic: str,
                 message: str,
                 thank_you_secret: bytes,
                 reply_pubkey: bytes,
                 ):
        self.topic = topic
        self.message = message
        self.thank_you_secret = thank_you_secret
        self.reply_pubkey = reply_pubkey

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

# %%


class BroadcastFrame(ReprObject):
    def __init__(self, ask_id: UUID,
                 message_frame: MessageFrame,
                 backward_onion: OnionRoute,
                 ):
        self.ask_id = ask_id
        self.message_frame = message_frame
        self.backward_onion = backward_onion


class POWBroadcastFrame(BroadcastFrame):
    def __init__(self, ask_id: UUID,
                 message_frame: MessageFrame,
                 backward_onion: OnionRoute,
                 proof_of_work: ProofOfWork,
                 ):
        super().__init__(ask_id,
                         message_frame,
                         backward_onion)
        self.proof_of_work = proof_of_work


class InvoicedBroadcastFrame(BroadcastFrame):
    def __init__(self, ask_id: UUID,
                 message_frame: MessageFrame,
                 backward_onion: OnionRoute,
                 proof_of_payment: ProofOfPayment,
                 ):
        super().__init__(ask_id,
                         message_frame,
                         backward_onion)
        self.proof_of_payment = proof_of_payment


# %%
class CommunicationFrame(ReprObject):
    def __init__(self, payload: Payload,
                 forward_onion: OnionRoute,
                 backward_onion: OnionRoute,
                 ):
        self.payload = payload
        self.forward_onion = forward_onion
        self.backward_onion = backward_onion


class POWCommunicationFrame(CommunicationFrame):
    def __init__(self, payload: Payload,
                 forward_onion: OnionRoute,
                 backward_onion: OnionRoute,
                 proof_of_work: ProofOfWork,
                 ):
        super().__init__(payload,
                         forward_onion,
                         backward_onion)
        self.proof_of_work = proof_of_work


class InvoicedCommunicationFrame(CommunicationFrame):
    def __init__(self, payload: Payload,
                 forward_onion: OnionRoute,
                 backward_onion: OnionRoute,
                 proof_of_payment: ProofOfPayment
                 ):
        super().__init__(payload,
                         forward_onion,
                         backward_onion)
        self.proof_of_payment = proof_of_payment
# %%


class Topic(ReprObject):
    def __init__(self, name: str, path: str, after: datetime, before: datetime) -> None:
        self.name = name
        self.path = path
        self.after = after
        self.before = before

    def sign(self, private_key: bytes):
        obj = (self.name, self.path, self.after, self.before)
        self.signature = crypto.sign_object(obj, private_key)

    def verify(self, public_key: bytes):
        obj = (self.name, self.path, self.after, self.before)
        return crypto.verify_object(obj, self.signature, public_key)

# %%


class SweetGossipNode(Agent):
    def __init__(self,
                 context_name,
                 name,
                 payment_channel: PaymentChannel):
        super().__init__(context_name, name)
        self.name = name
        self._private_key, self.public_key = crypto.create_keys()
        self.payment_channel = payment_channel
        self.history = []
        self._log_i = 0
        self._known_hosts = dict()
        self._asks_for = dict()
        self._already_seen = []

    def log_history(self, e, what, d={}):
        self.ctx(e, lambda: self.trace(e, str(d)))
        self.history.append(d)
        self._log_i += 1

    def connect_to(self, other):
        self._known_hosts[other.name] = other
        other._known_hosts[self.name] = self

    def broadcast(self, e,
                  certificate: Certificate,
                  topic: Topic,
                  backward_onion: OnionRoute = OnionRoute()):

        topic.sign(self._private_key)
        message_frame = MessageFrame(certificate,
                                     topic)
        aff_frame = AskForBroadcastFrame(
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
            com_frame = CommunicationFrame(
                payload, forward_onion, backward_onion)
            self.new_message(e, self._known_hosts[top_layer.name], com_frame)

    def respond(self, e, message: str, reward_satoshis: int, forward_onion: OnionRoute):
        payload = Payload()
        payload.encrypt(message, pub_key=self.pub_key)
        self.communicate(e, payload, reward_satoshis,
                         forward_onion=forward_onion,
                         backward_onion=OnionRoute())

    def on_message(self, e, m):
        if isinstance(m.data, AskForBroadcastFrame):
            aff_frame = m.data
            peer_name = m.sender.name
            fc_cond = POWBroadcastConditionsFrame(
                aff_frame.topic, aff_frame.buf_size,
                valid_till=datetime.now()+timedelta(minutes=10),
                pow_scheme="sha256",
                pow_target=pow.MAX_POW_TARGET_SHA256)
            self.reply(e, m, fc_cond)
        elif isinstance(m.data, POWBroadcastConditionsFrame):
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
                elif isinstance(brd_frame, InvoicedBroadcastFrame):
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
        elif isinstance(m.data, CommunicationFrame):
            com_frame = m.data
            self.communicate(e, payload=com_frame.payload,
                             onion=com_frame.onion)
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
    with Stopwatch() as sw:
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

    print(sw.total())


# %%
execute_simulation(main, sim_id=FOLDNAME)

# %%
