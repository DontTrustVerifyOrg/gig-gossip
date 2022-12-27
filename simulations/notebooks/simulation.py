# %%
from datetime import datetime, timedelta
from functools import partial
from typing import List, Dict

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


class OnionRoute:
    pass


class OnionRoute(ReprObject):
    def __init__(self) -> None:
        self._onion = b""

    def peel(self, priv_key: bytes) -> OnionLayer:
        layer, rest = crypto.decrypt_object(self._onion, priv_key)
        self._onion = rest
        return layer

    def grow(self, layer: OnionLayer, pub_key: bytes) -> None:
        self._onion = crypto.encrypt_object((layer, self._onion), pub_key)

    def clone(self) -> OnionRoute:
        onion = OnionRoute()
        onion._onion = self._onion
        return onion


# %%


class Topic(SignableObject):
    def __init__(self, id: UUID, name: str, path: str, after: datetime, before: datetime) -> None:
        self.id = id
        self.name = name
        self.path = path
        self.after = after
        self.before = before

# %%


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


class InvoicedBroadcastConditionsFrame(BroadcastConditionsFrame):
    def __init__(self, topic_id: UUID, valid_till: datetime, invoice: Invoice) -> None:
        super().__init__(topic_id, valid_till)
        self.invoice = invoice


class RoutePaymentLayer(ReprObject):
    def __init__(self, account: bytes, amount: int) -> None:
        self.account = account
        self.amount = amount


class BroadcastFrame(ReprObject):
    def __init__(self,
                 originator_certificate: Certificate,
                 signed_topic: Topic,
                 backward_onion: OnionRoute,
                 route_payment_list: List[RoutePaymentLayer]
                 ):
        self.originator_certificate = originator_certificate
        self.signed_topic = signed_topic
        self.backward_onion = backward_onion
        self.route_payment_list = route_payment_list


class POWBroadcastFrame(ReprObject):
    def __init__(self,
                 broadcast_frame: BroadcastFrame,
                 proof_of_work: ProofOfWork
                 ):
        self.broadcast_frame = broadcast_frame
        self.proof_of_work = proof_of_work


# %%


class PreimageList(ReprObject):
    def __init__(self, num: int) -> None:
        self.preimages = crypto.generate_symmetric_keys(num)

    def compute_payment_hashes(self):
        return [compute_payment_hash(preimage) for preimage in self.preimages]

    def pop(self) -> bytes:
        return self.preimages.pop()


class InvoiceBuilderSeed(SignableObject):
    def __init__(self,
                 route_payment_list: List[RoutePaymentLayer],
                 payment_hash_list: List[bytes]
                 ) -> None:
        self.route_payment_list = route_payment_list
        self.payment_hash_list = payment_hash_list


class ResponseFrame(ReprObject):
    def __init__(self,
                 replier_private_key: bytes,
                 replier_certificate: Certificate,
                 route_payment_list: List[RoutePaymentLayer],
                 forward_onion: OnionRoute,
                 originator_public_key: bytes,
                 message: bytes) -> None:
        self.replier_certificate = replier_certificate
        self.preimage_list = PreimageList(len(route_payment_list))
        self.invoice_builder_seed = InvoiceBuilderSeed(
            route_payment_list, self.preimage_list.compute_payment_hashes())
        self.invoice_builder_seed.sign(replier_private_key)
        self.forward_onion = forward_onion
        self.invoices: List[Invoice] = list()
        self.data = self.encrypt(message, originator_public_key)

    def pop_invoice(self, broadcaster_payment_channel: PaymentChannel, valid_till: datetime) -> Invoice:
        if self.replier_certificate.verify():
            if self.invoice_builder_seed.verify(self.replier_certificate.public_key):
                idx = len(self.preimage_list)
                layer = self.invoice_builder_seed.route_payment_list[idx]
                payment_hash = self.invoice_builder_seed.payment_hash_list[idx]
                if layer.account == broadcaster_payment_channel.account:
                    preimage = self.preimage_list.pop()
                    if compute_payment_hash(preimage) == payment_hash:
                        return broadcaster_payment_channel.create_invoice(layer.amount, preimage, valid_till)
        return None

    def encrypt(self, message: bytes, originator_public_key: bytes) -> bytes:
        data = message
        data = crypto.encrypt_object(data, originator_public_key)
        for key in self.preimage_list:
            data = crypto.symmetric_encrypt(key, data)
        return data

    def verify(self):
        if self.replier_certificate.verify():
            if self.invoice_builder_seed.verify(self.replier_certificate.public_key):
                payment_account_list_a = [
                    invoice.account for invoice in self.invoices]
                payment_account_list_b = [
                    layer.account for layer in self.invoice_builder_seed.route_payment_list]
                if payment_account_list_a == payment_account_list_b:
                    payment_amount_list_a = [
                        invoice.amount for invoice in self.invoices]
                    payment_amount_list_b = [
                        layer.amount for layer in self.invoice_builder_seed.route_payment_list]
                    if payment_amount_list_a == payment_amount_list_b:
                        payment_hash_list_a = [
                            invoice.payment_hash for invoice in self.invoices]
                        payment_hash_list_b = self.invoice_builder_seed.payment_hash_list
                        if payment_hash_list_a == payment_hash_list_b:
                            return True
        return False

    def contains_route_payment_layer(self, layer: RoutePaymentLayer) -> bool:
        if self.verify():
            for invoice in self.invoices:
                if invoice.account == layer.account and invoice.amount == layer.amount:
                    return True
        return False

    def pay(self, originator_payment_channel: PaymentChannel, originator_private_key: bytes) -> bytes:
        if self.verify():
            proofs_of_payments = [originator_payment_channel.pay_invoice(invoice)
                                  for invoice in self.invoices]
            message = self.data
            for key in reversed([pop.preimage for pop in proofs_of_payments]):
                message = crypto.symmetric_decrypt(key, message)

            return crypto.decrypt_object(message, originator_private_key)
        return None


# %%

class SweetGossipNode(Agent):
    def __init__(self,
                 context_name,
                 name,
                 certificate: Certificate,
                 private_key: bytes,
                 payment_channel: PaymentChannel):
        super().__init__(context_name, name)
        self.name = name
        self.certificate = certificate
        self.private_key = private_key
        self.payment_channel = payment_channel
        self.history = []
        self._log_i = 0
        self._known_hosts: Dict[str, str] = dict()
        self._asks_for: Dict[str, Dict[UUID, BroadcastFrame]] = dict()
        self._already_seen: List[UUID] = []

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
                  route_payment_list: List[RoutePaymentLayer] = list()):

        topic.sign(self._private_key)
        aff_frame = AskForBroadcastFrame(self.certificate, topic)
        self._already_seen.append(topic.id)
        for peer in self._known_hosts.values():
            if not peer.name in self._asks_for:
                self._asks_for[peer.name] = dict()
            backward_onion.enclose(OnionLayer(self.name), self._priv_key)
            route_payment_list.append(
                RoutePaymentLayer(self.payment_channel.account, 10))  # 10 satoshis
            brd = BroadcastFrame(self.certificate,
                                 topic,
                                 backward_onion,
                                 route_payment_list)
            self._asks_for[peer.name][topic.id] = brd
            self.new_message(e, peer, aff_frame)

    def on_ask_for_broadcast(self, e, m, peer_name: str, aff_frame: AskForBroadcastFrame):
        fc_cond = POWBroadcastConditionsFrame(
            topic_id=aff_frame.topic.id,
            valid_till=datetime.now()+timedelta(minutes=10),
            work_request=WorkRequest(pow_scheme="sha256",
                                     pow_target=pow_target_from_complexity(
                                         "sha256", 1)))
        self.reply(e, m, fc_cond)

    def on_pow_broadcast_conditions(self, e, m, peer_name: str, fc_cond: POWBroadcastConditionsFrame):
        if fc_cond.valid_till <= datetime.now():
            if fc_cond.topic_id in self._asks_for[peer_name]:
                brd_frame = self._asks_for[peer_name][fc_cond.topic_id]
                pow_brd = POWBroadcastFrame(brd_frame,
                                            fc_cond.work_request.compute_proof(
                                                brd_frame))
                self.reply(e, m, pow_brd)

    def accept_broadcast(self, brd_frame: BroadcastFrame) -> InvoiceBuilderFrame:
        reply_frame = ResponseFrame(
            replier_private_key=self.private_key,
            replier_certificate=self.certificate,
            route_payment_list=brd_frame.route_payment_list,
            forward_onion=brd_frame.backward_onion,
            originator_public_key=brd_frame.originator_certificate.public_key,
            message="hello"
        )
        return reply_frame

    def on_pow_broadcast(self, e, m, peer_name: str, pow_frame: POWBroadcastFrame):
        if pow_frame.proof_of_work.validate(pow_frame.broadcast_frame):
            ib_frame = self.accept_broadcast(pow_frame.broadcast_frame)
            if ib_frame is not None:
                self.on_response(e, m, peer_name, ib_frame=ib_frame)
            else:
                self.broadcast(e, topic=pow_frame.topic,
                               backward_onion=pow_frame.broadcast_frame.backward_onion,
                               route_payment_list=pow_frame.broadcast_frame.route_payment_list)

    def on_response(self, e, m, peer_name: str, response_frame: ResponseFrame):
        top_layer = response_frame.forward_onion.peel()
        if top_layer.name == self.name:
            self.on_response_received(response_frame)
        elif top_layer.name in self._known_hosts:
            invoice = response_frame.pop_invoice(
                self.payment_channel, datetime.now()+timedelta.days(1))
            response_frame.invoices.append(invoice)
            self.new_message(
                e, self._known_hosts[top_layer.name], response_frame)

    def on_response_received(self, response_frame: ResponseFrame):
        message = response_frame.pay(self.payment_channel, self.private_key)

    def on_message(self, e, m):
        if isinstance(m.data, AskForBroadcastFrame):
            self.on_ask_for_broadcast(e, m, m.sender, m.data)
        elif isinstance(m.data, POWBroadcastConditionsFrame):
            self.on_pow_broadcast_conditions(e, m, m.sender, m.data)
        elif isinstance(m.data, POWBroadcastFrame):
            self.on_pow_broadcast(e, m, m.sender, m.data)
        elif isinstance(m.data, ResponseFrame):
            self.on_response(e, m, m.sender, m.data)
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
