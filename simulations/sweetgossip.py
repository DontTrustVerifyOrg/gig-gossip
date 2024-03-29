from __future__ import annotations
from copy import deepcopy

from datetime import datetime, timedelta
from typing import Callable, Dict, List, Set, Tuple
from uuid import UUID, uuid4

import crypto
from cert import Certificate
from mass import Agent
from myrepr import ReprObject
from payments import HodlInvoice, Invoice, PaymentChannel, compute_payment_hash
from pow import ProofOfWork, WorkRequest, pow_target_from_complexity

from numpy import argmin


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


class AbstractTopic(ReprObject):
    pass


class RequestPayload(SignableObject):
    def __init__(self, id: UUID, topic: AbstractTopic, sender_certificate: Certificate) -> None:
        self.payload_id = id
        self.topic = topic
        self.sender_certificate = sender_certificate


class AskForBroadcastFrame(ReprObject):
    def __init__(self, signed_request_payload: RequestPayload) -> None:
        self.ask_id = uuid4()
        self.signed_request_payload = signed_request_payload


class POWBroadcastConditionsFrame(ReprObject):
    def __init__(self, ask_id: UUID, valid_till: datetime, work_request: WorkRequest, timestamp_tolerance: timedelta) -> None:
        self.ask_id = ask_id
        self.valid_till = valid_till
        self.work_request = work_request
        self.timestamp_tolerance = timestamp_tolerance


class BroadcastPayload(ReprObject):
    def __init__(self,
                 signed_request_payload: RequestPayload,
                 backward_onion: OnionRoute
                 ) -> None:
        self.signed_request_payload = signed_request_payload
        self.backward_onion = backward_onion
        self.timestamp = None

    def set_timestamp(self, timestamp: datetime):
        self.timestamp = timestamp


class POWBroadcastFrame(ReprObject):
    def __init__(self,
                 ask_id: UUID,
                 broadcast_payload: BroadcastPayload,
                 proof_of_work: ProofOfWork
                 ):
        self.ask_id = ask_id
        self.broadcast_payload = broadcast_payload
        self.proof_of_work = proof_of_work

    def verify(self) -> bool:
        if not self.broadcast_payload.signed_request_payload.sender_certificate.verify():
            return False

        if not self.broadcast_payload.signed_request_payload.verify(self.broadcast_payload.signed_request_payload.sender_certificate.public_key):
            return False

        return self.proof_of_work.validate(self.broadcast_payload)


class SettlementPromise(SignableObject):
    def __init__(self,
                 settler_certificate: Certificate,
                 network_payment_hash: bytes,
                 hash_of_encrypted_reply_payload: bytes,
                 reply_payment_amount: int
                 ) -> None:
        self.settler_certificate = settler_certificate
        self.network_payment_hash = network_payment_hash
        self.hash_of_encrypted_reply_payload = hash_of_encrypted_reply_payload
        self.reply_payment_amount = reply_payment_amount

    def verify_all(self, encrypted_signed_reply_payload: bytes) -> bool:
        if not self.settler_certificate.verify():
            return False
        if not self.verify(self.settler_certificate.public_key):
            return False
        if crypto.compute_sha256([encrypted_signed_reply_payload]) != self.hash_of_encrypted_reply_payload:
            return False
        return True


class ReplyPayload(ReprObject):
    def __init__(self,
                 replier_certificate: Certificate,
                 signed_request_payload: RequestPayload,
                 encrypted_reply_message: bytes,
                 reply_invoice: HodlInvoice
                 ) -> None:
        self.replier_certificate = replier_certificate
        self.signed_request_payload = signed_request_payload
        self.encrypted_reply_message = encrypted_reply_message
        self.reply_invoice = reply_invoice

    def verify_all(self):
        if not self.replier_certificate.verify():
            return False
        if not self.signed_request_payload.sender_certificate.verify():
            return False
        if not self.signed_request_payload.verify(self.signed_request_payload.sender_certificate.public_key):
            return False
        return True


class ReplyFrame(ReprObject):
    def __init__(self,
                 encrypted_reply_payload: bytes,
                 signed_settlement_promise: SettlementPromise,
                 forward_onion: OnionRoute,
                 network_invoice: HodlInvoice) -> None:

        self.encrypted_reply_payload = encrypted_reply_payload
        self.signed_settlement_promise = signed_settlement_promise
        self.forward_onion = forward_onion
        self.network_invoice = network_invoice

    def decrypt_and_verify(self, sender_private_key: bytes) -> ReplyPayload:
        reply_payload: ReplyPayload = crypto.decrypt_object(
            self.encrypted_reply_payload, sender_private_key)

        if not reply_payload.replier_certificate.verify():
            return None
        if not reply_payload.verify_all():
            return None
        return reply_payload


InvoiceById: Dict[UUID, Tuple[PaymentChannel, bytes]] = dict()


def SetSettementCommand(payment_channel: PaymentChannel, invoice_id: UUID, preimage) -> None:
    global InvoiceById
    InvoiceById[invoice_id] = (payment_channel, preimage)


def OnSettementCommand(invoice: HodlInvoice) -> None:
    global InvoiceById
    payment_channel,  preimage = InvoiceById[invoice.id]
    payment_channel.settle_hodl_invoice(invoice, preimage)


class Settler:

    def __init__(self,
                 settler_certificate: Certificate,
                 settler_private_key: bytes,
                 payment_channel: PaymentChannel,
                 price_amount_for_settlement: int,
                 ) -> None:
        self.settler_certificate = settler_certificate
        self._settler_private_key = settler_private_key
        self.payment_channel = payment_channel
        self.price_amount_for_settlement = price_amount_for_settlement

    def generate_reply_payment_trust(self) -> Tuple[bytes, Callable[[HodlInvoice]]]:
        reply_preimage = crypto.generate_symmetric_key()
        reply_payment_hash = compute_payment_hash(reply_preimage)

        invoice_id = uuid4()
        SetSettementCommand(self.payment_channel, invoice_id, reply_preimage)
        return invoice_id, reply_payment_hash, OnSettementCommand

    def generate_settlement_trust(self, message: bytes, reply_invoice: HodlInvoice, signed_request_payload: RequestPayload, replier_certificate: Certificate) -> Tuple[HodlInvoice, SettlementPromise, bytes]:

        network_preimage = crypto.generate_symmetric_key()
        network_payment_hash = compute_payment_hash(network_preimage)

        encrypted_reply_message = crypto.symmetric_encrypt(
            network_preimage, message)

        network_payment_hash = compute_payment_hash(
            network_preimage)

        def on_accepted(i: Invoice):
            self.payment_channel.settle_hodl_invoice(
                i, network_preimage)

        network_invoice = self.payment_channel.create_hodl_invoice(
            self.price_amount_for_settlement,
            network_payment_hash,
            on_accepted=on_accepted
        )

        reply_payload = ReplyPayload(replier_certificate,
                                     signed_request_payload,
                                     encrypted_reply_message,
                                     reply_invoice)

        encrypted_reply_payload = crypto.encrypt_object(
            reply_payload, signed_request_payload.sender_certificate.public_key)

        hash_of_encrypted_reply_payload = crypto.compute_sha256(
            [encrypted_reply_payload])
        signed_settlement_promise = SettlementPromise(
            self.settler_certificate, network_payment_hash, hash_of_encrypted_reply_payload, reply_invoice.amount)
        signed_settlement_promise.sign(self._settler_private_key)
        return signed_settlement_promise, network_invoice, encrypted_reply_payload


class SweetGossipNode(Agent):
    def __init__(self,
                 name,
                 certificate: Certificate,
                 private_key: bytes,
                 payment_channel: PaymentChannel,
                 price_amount_for_routing: int,
                 broadcast_conditions_timeout: timedelta,
                 broadcast_conditions_pow_scheme: str,
                 broadcast_conditions_pow_complexity: int,
                 timestamp_tolerance: timedelta,
                 invoice_payment_timeout: timedelta,
                 settler: Settler,
                 ):
        super().__init__(name)
        self.name = name
        self.certificate = certificate
        self._private_key = private_key
        self.payment_channel = payment_channel
        self.price_amount_for_routing = price_amount_for_routing
        self.broadcast_conditions_timeout = broadcast_conditions_timeout
        self.broadcast_conditions_pow_scheme = broadcast_conditions_pow_scheme
        self.broadcast_conditions_pow_complexity = broadcast_conditions_pow_complexity
        self.timestamp_tolerance = timestamp_tolerance
        self.invoice_payment_timeout = invoice_payment_timeout
        self.settler = settler

        self._known_hosts: Dict[str, SweetGossipNode] = dict()
        self._broadcast_payloads_by_ask_id: Dict[UUID, BroadcastPayload] = dict(
        )
        self._my_pow_br_cond_by_ask_id: Dict[UUID,
                                             POWBroadcastConditionsFrame] = dict()
        self._already_broadcasted_request_payload_ids: Dict[UUID, int] = dict()
        self.reply_payloads: Dict[UUID,
                                  Dict[bytes,
                                       List[Tuple[ReplyPayload, HodlInvoice]]]] = dict()

    def connect_to(self, other):
        if other.name == self.name:
            raise Exception("Cannot connect node to itself")
        self._known_hosts[other.name] = other
        other._known_hosts[self.name] = self

    def accept_topic(self, topic: AbstractTopic) -> bool:
        return False

    def increment_broadcasted(self, payload_id: int) -> None:
        if not payload_id in self._already_broadcasted_request_payload_ids:
            self._already_broadcasted_request_payload_ids[payload_id] = 0
        self._already_broadcasted_request_payload_ids[payload_id] += 1

    def can_increment_broadcast(self, payload_id: int) -> bool:
        if not payload_id in self._already_broadcasted_request_payload_ids:
            return True
        return self._already_broadcasted_request_payload_ids[payload_id] <= 2

    def broadcast(self, e,
                  request_payload: RequestPayload,
                  originator_peer_name: str = None,
                  backward_onion: OnionRoute = OnionRoute()):
        if not self.accept_topic(request_payload.topic):
            return

        self.increment_broadcasted(request_payload.payload_id)

        if not self.can_increment_broadcast(request_payload.payload_id):
            self.info(e, "already broadcasted")
            return

        for peer in self._known_hosts.values():
            if peer.name == originator_peer_name:
                continue
            print(self.name, "================>>>>>>>>>", peer.name)
            ask_for_broadcast_frame = AskForBroadcastFrame(request_payload)
            broadcast_payload = BroadcastPayload(request_payload,
                                                 backward_onion.grow(OnionLayer(
                                                     self.name), peer.certificate.public_key))
            self._broadcast_payloads_by_ask_id[ask_for_broadcast_frame.ask_id] = broadcast_payload
            self.new_message(e, peer, ask_for_broadcast_frame)

    def on_ask_for_broadcast_frame(self, e, m, peer: SweetGossipNode, ask_for_broadcast_frame: AskForBroadcastFrame):
        if not self.can_increment_broadcast(ask_for_broadcast_frame.signed_request_payload.payload_id):
            self.info(e, "already broadcasted dont ask")
            return
        pow_broadcast_conditions_frame = POWBroadcastConditionsFrame(
            ask_id=ask_for_broadcast_frame.ask_id,
            valid_till=datetime.now()+self.broadcast_conditions_timeout,
            work_request=WorkRequest(pow_scheme=self.broadcast_conditions_pow_scheme,
                                     pow_target=pow_target_from_complexity(
                                         self.broadcast_conditions_pow_scheme, self.broadcast_conditions_pow_complexity)),
            timestamp_tolerance=self.timestamp_tolerance)
        self._my_pow_br_cond_by_ask_id[pow_broadcast_conditions_frame.ask_id] = pow_broadcast_conditions_frame
        self.new_message(e, peer, pow_broadcast_conditions_frame)

    def on_pow_broadcast_conditions_frame(self, e, m, peer: SweetGossipNode, pow_broadcast_condtitions_frame: POWBroadcastConditionsFrame):
        if datetime.now() <= pow_broadcast_condtitions_frame.valid_till:
            if pow_broadcast_condtitions_frame.ask_id in self._broadcast_payloads_by_ask_id:
                broadcast_payload = self._broadcast_payloads_by_ask_id[
                    pow_broadcast_condtitions_frame.ask_id]
                broadcast_payload.set_timestamp(datetime.now())
                pow = pow_broadcast_condtitions_frame.work_request.compute_proof(
                    broadcast_payload)
                pow_broadcast_frame = POWBroadcastFrame(pow_broadcast_condtitions_frame.ask_id,
                                                        broadcast_payload,
                                                        pow)
                self.new_message(e, peer, pow_broadcast_frame)

    def accept_broadcast(self, signed_request_payload: RequestPayload) -> Tuple[bytes, int]:
        return None, 0

    def on_pow_broadcast_frame(self, e, m, peer: SweetGossipNode, pow_broadcast_frame: POWBroadcastFrame):

        if not pow_broadcast_frame.ask_id in self._my_pow_br_cond_by_ask_id:
            return

        my_pow_broadcast_condition_frame = self._my_pow_br_cond_by_ask_id[
            pow_broadcast_frame.ask_id]

        if pow_broadcast_frame.proof_of_work.pow_scheme != my_pow_broadcast_condition_frame.work_request.pow_scheme:
            return

        if pow_broadcast_frame.proof_of_work.pow_target != my_pow_broadcast_condition_frame.work_request.pow_target:
            return

        if pow_broadcast_frame.broadcast_payload.timestamp > datetime.now():
            return

        if pow_broadcast_frame.broadcast_payload.timestamp+my_pow_broadcast_condition_frame.timestamp_tolerance < datetime.now():
            return

        if not pow_broadcast_frame.verify():
            return

        message, fee = self.accept_broadcast(
            pow_broadcast_frame.broadcast_payload.signed_request_payload)

        if message is not None:

            invoice_id, reply_payment_hash, on_accepted = self.settler.generate_reply_payment_trust()

            reply_invoice = self.payment_channel.create_hodl_invoice(
                fee, reply_payment_hash, on_accepted, invoice_id=invoice_id)

            signed_settlement_promise, network_invoice, encrypted_reply_payload = self.settler.generate_settlement_trust(
                message=message,
                reply_invoice=reply_invoice,
                signed_request_payload=pow_broadcast_frame.broadcast_payload.signed_request_payload,
                replier_certificate=self.certificate)

            response_frame = ReplyFrame(
                encrypted_reply_payload=encrypted_reply_payload,
                signed_settlement_promise=signed_settlement_promise,
                forward_onion=pow_broadcast_frame.broadcast_payload.backward_onion,
                network_invoice=network_invoice
            )

            self.on_response_frame(
                e, m, peer, response_frame=response_frame, new_response=True)
        else:
            self.broadcast(e, request_payload=pow_broadcast_frame.broadcast_payload.signed_request_payload,
                           originator_peer_name=peer.name,
                           backward_onion=pow_broadcast_frame.broadcast_payload.backward_onion)

    def on_response_frame(self, e, m, peer: SweetGossipNode, response_frame: ReplyFrame, new_response: bool = False):
        if response_frame.forward_onion.is_empty():
            if response_frame.signed_settlement_promise.network_payment_hash != response_frame.network_invoice.payment_hash:
                self.error(
                    e, "reply payload has different network_payment_hash than network_invoice")
                return

            reply_payload = response_frame.decrypt_and_verify(
                self._private_key)
            if reply_payload is None:
                self.error(e, "reply payload mismatch")
                return
            payload_id = reply_payload.signed_request_payload.payload_id
            if not payload_id in self.reply_payloads:
                self.reply_payloads[payload_id] = dict()
            replier_id = reply_payload.replier_certificate.public_key
            if not replier_id in self.reply_payloads[payload_id]:
                self.reply_payloads[payload_id][replier_id] = list()

            self.reply_payloads[payload_id][replier_id].append(
                (reply_payload, response_frame.network_invoice))
            self.info(e, "reply payload frame collected")
        else:
            top_layer = response_frame.forward_onion.peel(
                self._private_key)
            if top_layer.peer_name in self._known_hosts:
                if not response_frame.signed_settlement_promise.verify_all(response_frame.encrypted_reply_payload):
                    return
                if response_frame.signed_settlement_promise.network_payment_hash != response_frame.network_invoice.payment_hash:
                    return
                network_invoice = None
                if not new_response:
                    next_network_invoice = response_frame.network_invoice

                    def on_accepted(i: HodlInvoice):
                        def on_settled(j: HodlInvoice, preimage: bytes):
                            self.payment_channel.settle_hodl_invoice(
                                i, preimage)

                        self.payment_channel.pay_hodl_invoice(next_network_invoice,
                                                              on_settled,
                                                              )

                    network_invoice = self.payment_channel.create_hodl_invoice(
                        response_frame.network_invoice.amount+self.price_amount_for_routing,
                        response_frame.network_invoice.payment_hash,
                        on_accepted,
                    )

                    response_frame = deepcopy(response_frame)
                    response_frame.network_invoice = network_invoice
                self.new_message(
                    e, self._known_hosts[top_layer.peer_name], response_frame)

    def get_responses(self, e, payload_id: UUID) -> List[List[Tuple[ReplyPayload, HodlInvoice]]]:
        if not payload_id in self.reply_payloads:
            self.error(e, "topic has no responses")
            return list()
        return [list(response_frame_list) for response_frame_list in self.reply_payloads[payload_id].values()]

    def pay_and_read_response(self, e, reply_payload: ReplyPayload, network_invoice: HodlInvoice):
        payload_id = reply_payload.signed_request_payload.payload_id
        if not payload_id in self.reply_payloads:
            self.error(e, "topic has no responses")
            return

        if not reply_payload.replier_certificate.public_key in self.reply_payloads[payload_id]:
            self.error(e, "replier has not responsed for this topic")
            return

        self.info(e, "paying and reading")

        def on_settled(_: HodlInvoice, preimage: bytes):
            message = crypto.symmetric_decrypt(preimage,
                                               reply_payload.encrypted_reply_message)

            self.info(e, message)

        self.payment_channel.pay_hodl_invoice(
            network_invoice,
            on_settled=on_settled)

    def on_message(self, e, m):
        if isinstance(m.data, AskForBroadcastFrame):
            self.on_ask_for_broadcast_frame(e, m, m.sender, m.data)
        elif isinstance(m.data, POWBroadcastConditionsFrame):
            self.on_pow_broadcast_conditions_frame(e, m, m.sender, m.data)
        elif isinstance(m.data, POWBroadcastFrame):
            self.on_pow_broadcast_frame(e, m, m.sender, m.data)
        elif isinstance(m.data, ReplyFrame):
            self.on_response_frame(e, m, m.sender, m.data)
        else:
            self.trace(e, "unknown request:", m)
