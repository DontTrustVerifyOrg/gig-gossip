# %%
from __future__ import annotations
from datetime import datetime, timedelta
from functools import partial
from typing import List, Dict, Set

import simpy
from tqdm.auto import tqdm

import crypto
from mass import Agent, execute_simulation, simulate, trace
from mass_tools import time_to_int
from stopwatch import Stopwatch
from datetime import datetime

from cert import Certificate, CertificationAuthority, create_certification_authority, get_certification_authority_by_name
from payments import PaymentChannel, Invoice, ProofOfPayment, compute_payment_hash
from pow import WorkRequest, ProofOfWork, pow_target_from_complexity

from experiment_tools import FOLDNAME, RUN_START
from myrepr import ReprObject

from uuid import UUID, uuid4
# %%


class SignableObject(ReprObject):
    def sign(self, private_key: bytes) -> None:
        self.signature = None
        self.signature = crypto.sign_object(self, private_key)

    def verify(self, public_key: bytes) -> bool:
        signature = self.signature
        self.signature = None
        result = crypto.verify_object(self, signature, public_key)
        self.signature = signature
        return result


class OnionLayer(ReprObject):
    def __init__(self, peer_name: str) -> None:
        self.peer_name = peer_name


class OnionRoute(ReprObject):
    def __init__(self) -> None:
        self._onion = b""

    def peel(self, priv_key: bytes) -> OnionLayer:
        layer, rest = crypto.decrypt_object(self._onion, priv_key)
        self._onion = rest
        return layer

    def grow(self, layer: OnionLayer, pub_key: bytes) -> OnionRoute:
        new_onion = OnionRoute()
        new_onion._onion = crypto.encrypt_object((layer, self._onion), pub_key)
        return new_onion

    def is_empty(self) -> bool:
        return len(self._onion) == 0


class Topic(SignableObject):
    def __init__(self, id: UUID, name: str, path: str, after: datetime, before: datetime) -> None:
        self.id = id
        self.name = name
        self.path = path
        self.after = after
        self.before = before


class AskForBroadcastFrame(ReprObject):
    def __init__(self, originator_certificate: Certificate, signed_topic: Topic) -> None:
        self.originator_certificate = originator_certificate
        self.signed_topic = signed_topic


class BroadcastConditionsFrame(ReprObject):
    def __init__(self, topic_id: UUID,  valid_till: datetime) -> None:
        self.topic_id = topic_id
        self.valid_till = valid_till


class POWBroadcastConditionsFrame(BroadcastConditionsFrame):
    def __init__(self, topic_id: UUID, valid_till: datetime, work_request: WorkRequest) -> None:
        super().__init__(topic_id, valid_till)
        self.work_request = work_request


class RoutingPaymentInstruction(ReprObject):
    def __init__(self, account: bytes, amount: int) -> None:
        self.account = account
        self.amount = amount


class BroadcastPayload(ReprObject):
    def __init__(self,
                 originator_certificate: Certificate,
                 signed_topic: Topic,
                 backward_onion: OnionRoute,
                 routing_payment_instruction_list: List[RoutingPaymentInstruction]
                 ):
        self.originator_certificate = originator_certificate
        self.signed_topic = signed_topic
        self.backward_onion = backward_onion
        self.routing_payment_instruction_list = routing_payment_instruction_list


class POWBroadcastFrame(ReprObject):
    def __init__(self,
                 broadcast_payload: BroadcastPayload,
                 proof_of_work: ProofOfWork
                 ):
        self.broadcast_payload = broadcast_payload
        self.proof_of_work = proof_of_work

    def verify(self) -> bool:
        if not self.broadcast_payload.originator_certificate.verify():
            return False

        if not self.broadcast_payload.signed_topic.verify(self.broadcast_payload.originator_certificate.public_key):
            return False

        return self.proof_of_work.validate(self.broadcast_payload):


class PaymentStone(SignableObject):
    def __init__(self,
                 routing_payment_instruction_list: List[RoutingPaymentInstruction],
                 payment_hash_list: List[bytes]
                 ) -> None:
        self.routing_payment_instruction_list = routing_payment_instruction_list
        self.payment_hash_list = payment_hash_list


class ResponseFrame(ReprObject):
    def __init__(self,
                 replier_private_key: bytes,
                 replier_certificate: Certificate,
                 routing_payment_instruction_list: List[RoutingPaymentInstruction],
                 forward_onion: OnionRoute,
                 signed_topic: Topic,
                 originator_certificate: Certificate,
                 message: bytes) -> None:
        self.replier_certificate = replier_certificate
        self.preimage_list = crypto.generate_symmetric_keys(
            len(routing_payment_instruction_list))
        self.payment_stone = PaymentStone(routing_payment_instruction_list,
                                          [compute_payment_hash(preimage) for preimage in self.preimage_list])
        self.payment_stone.sign(replier_private_key)
        self.forward_onion = forward_onion
        self.signed_topic = signed_topic
        self.originator_certificate = originator_certificate
        self.invoices: List[Invoice] = list()
        self.data = self._encrypt(message, originator_certificate.public_key)

    def pop_invoice(self, broadcaster_payment_channel: PaymentChannel, valid_till: datetime) -> Invoice:
        idx = len(self.preimage_list)
        layer = self.payment_stone.routing_payment_instruction_list[idx]
        payment_hash = self.payment_stone.payment_hash_list[idx]
        if layer.account == broadcaster_payment_channel.account:
            preimage = self.preimage_list.pop()
            if compute_payment_hash(preimage) == payment_hash:
                return broadcaster_payment_channel.create_invoice(layer.amount, preimage, valid_till)
        return None

    def _encrypt(self, message: bytes, originator_public_key: bytes) -> bytes:
        data = message
        data = crypto.encrypt_object(data, originator_public_key)
        for key in self.preimage_list:
            data = crypto.symmetric_encrypt(key, data)
        return data

    def verify(self):
        if not self.originator_certificate.verify():
            return False
        if not self.signed_topic.verify(self.originator_certificate.public_key):
            return False
        if not self.replier_certificate.verify():
            return False
        if not self.payment_stone.verify(self.replier_certificate.public_key):
            return False
        payment_account_list_a = [invoice.account for invoice in self.invoices]
        payment_account_list_b = [
            layer.account for layer in self.payment_stone.routing_payment_instruction_list]
        if payment_account_list_a != payment_account_list_b:
            return False
        payment_amount_list_a = [invoice.amount for invoice in self.invoices]
        payment_amount_list_b = [
            layer.amount for layer in self.payment_stone.routing_payment_instruction_list]
        if payment_amount_list_a != payment_amount_list_b:
            return False
        payment_hash_list_a = [
            invoice.payment_hash for invoice in self.invoices]
        payment_hash_list_b = self.payment_stone.payment_hash_list
        if payment_hash_list_a != payment_hash_list_b:
            return False
        return True

    def contains_route_payment_layer(self, layer: RoutingPaymentInstruction) -> bool:
        for invoice in self.invoices:
            if invoice.account == layer.account and invoice.amount == layer.amount:
                return True
        return False

    def pay(self, originator_payment_channel: PaymentChannel, originator_private_key: bytes) -> bytes:
        proofs_of_payments = [originator_payment_channel.pay_invoice(invoice)
                              for invoice in self.invoices]
        message = self.data
        for key in reversed([pop.preimage for pop in proofs_of_payments]):
            message = crypto.symmetric_decrypt(key, message)

        return crypto.decrypt_object(message, originator_private_key)


# %%

class SweetGossipNode(Agent):
    def __init__(self,
                 context_name,
                 name,
                 certificate: Certificate,
                 private_key: bytes,
                 payment_channel: PaymentChannel,
                 price_amount_for_routing: int,
                 broadcast_conditions_timeout: timedelta,
                 broadcast_conditions_pow_scheme: str,
                 broadcast_conditions_pow_complexity: int):
        super().__init__(context_name, name)
        self.name = name
        self.certificate = certificate
        self.private_key = private_key
        self.payment_channel = payment_channel
        self.price_amount_for_routing = price_amount_for_routing
        self.broadcast_conditions_timeout = broadcast_conditions_timeout
        self.broadcast_conditions_pow_scheme = broadcast_conditions_pow_scheme
        self.broadcast_conditions_pow_complexity = broadcast_conditions_pow_complexity

        self._known_hosts: Dict[str, SweetGossipNode] = dict()
        self._broadcast_payloads_by_host_by_id: Dict[str,
                                                     Dict[UUID, BroadcastPayload]] = dict()
        self._already_seen_ids: Set[UUID] = set()

        self.history = []
        self._log_i = 0

    def log_history(self, e, what, d={}):
        self.ctx(e, lambda: self.trace(e, str(d)))
        self.history.append(d)
        self._log_i += 1

    def connect_to(self, other):
        self._known_hosts[other.name] = other
        other._known_hosts[self.name] = self

    def broadcast(self, e,
                  topic: Topic,
                  backward_onion: OnionRoute = OnionRoute(),
                  routing_payment_instruction_list: List[RoutingPaymentInstruction] = list()):
        if topic.id in self._already_seen_ids:
            return
        self._already_seen_ids.add(topic.id)
        topic.sign(self._private_key)
        ask_for_broadcast_frame = AskForBroadcastFrame(
            self.certificate, topic)
        for peer in self._known_hosts.values():
            if not peer.name in self._broadcast_payloads_by_host_by_id:
                self._broadcast_payloads_by_host_by_id[peer.name] = dict()
            routing_payment_instruction_list.append(
                RoutingPaymentInstruction(self.payment_channel.account, self.price_amount_for_routing))
            broadcast_payload = BroadcastPayload(self.certificate,
                                                 topic,
                                                 backward_onion.grow(OnionLayer(
                                                     self.name), peer.certificate.public_key),
                                                 routing_payment_instruction_list)
            self._broadcast_payloads_by_host_by_id[peer.name][topic.id] = broadcast_payload
            self.new_message(e, peer, ask_for_broadcast_frame)

    def on_ask_for_broadcast_frame(self, e, m, peer_name: str, ask_for_broadcast_frame: AskForBroadcastFrame):
        pow_broadcast_conditions_frame = POWBroadcastConditionsFrame(
            topic_id=ask_for_broadcast_frame.topic.id,
            valid_till=datetime.now()+self.broadcast_conditions_timeout,
            work_request=WorkRequest(pow_scheme=self.broadcast_conditions_pow_scheme,
                                     pow_target=pow_target_from_complexity(
                                         self.broadcast_conditions_pow_scheme, self.broadcast_conditions_pow_complexity)))
        self.reply(e, m, pow_broadcast_conditions_frame)

    def on_pow_broadcast_conditions_frame(self, e, m, peer_name: str, pow_broadcast_condtitions_frame: POWBroadcastConditionsFrame):
        if pow_broadcast_condtitions_frame.valid_till <= datetime.now():
            if peer_name in self._broadcast_payloads_by_host_by_id:
                if pow_broadcast_condtitions_frame.topic_id in self._broadcast_payloads_by_host_by_id[peer_name]:
                    broadcast_payload = self._broadcast_payloads_by_host_by_id[
                        peer_name][pow_broadcast_condtitions_frame.topic_id]
                    pow_broadcast_frame = POWBroadcastFrame(broadcast_payload,
                                                            pow_broadcast_condtitions_frame.work_request.compute_proof(
                                                                broadcast_payload))
                    self.reply(e, m, pow_broadcast_frame)

    def accept_broadcast(self, broadcast_payload: BroadcastPayload) -> ResponseFrame:
        reply_frame = ResponseFrame(
            replier_private_key=self.private_key,
            replier_certificate=self.certificate,
            routing_payment_instruction_list=broadcast_payload.routing_payment_instruction_list,
            forward_onion=broadcast_payload.backward_onion,
            signed_topic=broadcast_payload.signed_topic,
            originator_public_key=broadcast_payload.originator_certificate,
            message=bytes(f"mynameis={self.name}", encoding="utf8")
        )
        return reply_frame

    def on_pow_broadcast_frame(self, e, m, peer_name: str, pow_broadcast_frame: POWBroadcastFrame):
        if not pow_broadcast_frame.verify():
            return

        response_frame = self.accept_broadcast(
            pow_broadcast_frame.broadcast_payload)
        if response_frame is not None:
            self.on_response_frame(
                e, m, peer_name, response_frame=response_frame)
        else:
            self.broadcast(e, topic=pow_broadcast_frame.topic,
                           backward_onion=pow_broadcast_frame.broadcast_payload.backward_onion,
                           routing_payment_instruction_list=pow_broadcast_frame.broadcast_payload.routing_payment_instruction_list)

    def on_response_frame(self, e, m, peer_name: str, response_frame: ResponseFrame):
        if not response_frame.verify():
            return

        if response_frame.forward_onion.is_empty():
            message = response_frame.pay(
                self.payment_channel, self.private_key)
        else:
            if response_frame.contains_route_payment_layer(RoutingPaymentInstruction(self.payment_channel.account), self.price_amount_for_routing):
                top_layer = response_frame.forward_onion.peel(self.private_key)
                if top_layer.name in self._known_hosts:
                    invoice = response_frame.pop_invoice(
                        self.payment_channel, datetime.now()+timedelta.days(1))
                    response_frame.invoices.append(invoice)
                    self.new_message(
                        e, self._known_hosts[top_layer.name], response_frame)

    def on_message(self, e, m):
        if isinstance(m.data, AskForBroadcastFrame):
            self.on_ask_for_broadcast_frame(e, m, m.sender, m.data)
        elif isinstance(m.data, POWBroadcastConditionsFrame):
            self.on_pow_broadcast_conditions_frame(e, m, m.sender, m.data)
        elif isinstance(m.data, POWBroadcastFrame):
            self.on_pow_broadcast_frame(e, m, m.sender, m.data)
        elif isinstance(m.data, ResponseFrame):
            self.on_response_frame(e, m, m.sender, m.data)
        else:
            self.ctx(e, lambda: self.trace(e, "unknown request:", m))


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
