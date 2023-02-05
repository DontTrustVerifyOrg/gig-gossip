# %%
from typing import Dict, Set, Tuple
from mass import simulate
from mass_tools import time_to_int
from experiment_tools import FOLDNAME, RUN_START
from myrepr import ReprObject

from stopwatch import Stopwatch
from datetime import datetime, timedelta
from cert import CertificationAuthority, create_certification_authority
import crypto
from payments import PaymentChannel

from uuid import uuid4
from sweetgossip import SweetGossipNode, RequestPayload, AbstractTopic, Settler
from functools import partial
import simpy
import itertools

from enum import Enum
import random

import pygeohash as pgh


PAYANDREAD_TIME = time_to_int(2, 8, 0)


class TaxiTopic(AbstractTopic):
    def __init__(self, from_geohash: str,  to_geohash: str, pickup_after: datetime, dropoff_before: datetime) -> None:
        self.from_geohash = from_geohash
        self.to_geohash = to_geohash
        self.pickup_after = pickup_after
        self.dropoff_before = dropoff_before


class Gossiper(SweetGossipNode):
    def __init__(self, name, ca: CertificationAuthority, price_amount_for_routing, settler: Settler):
        private_key, public_key = crypto.generate_asymetric_keys()
        certificate = ca.issue_certificate(public_key, "is_ok", True, not_valid_after=datetime.now(
        )+timedelta(days=7), not_valid_before=datetime.now()-timedelta(days=7))
        payment_channel = PaymentChannel()
        super().__init__(name, certificate, private_key, payment_channel, price_amount_for_routing,
                         broadcast_conditions_timeout=timedelta(days=7), broadcast_conditions_pow_scheme="sha256", broadcast_conditions_pow_complexity=1, invoice_payment_timeout=timedelta(days=1),
                         timestamp_tolerance=timedelta(seconds=10),
                         settler=settler)

    def accept_topic(self, topic: AbstractTopic) -> bool:
        if isinstance(topic, TaxiTopic):
            return len(topic.from_geohash) >= 7 and len(topic.to_geohash) >= 7 and datetime.now() <= topic.dropoff_before
        return False


class GigWorker(Gossiper):
    def accept_broadcast(self, signed_topic: RequestPayload) -> Tuple[bytes, int]:
        return bytes(f"mynameis={self.name}", encoding="utf8"), 4321


class Customer(Gossiper):

    def homeostasis(self, e):
        self.trace(e, "is starting...")

        self.schedule(partial(self.run_job, {"run"}),
                      partial(self.on_return, e), RUN_START)

        self.schedule(partial(self.payandread_job, {"pay&read"}),
                      partial(self.on_return, e), PAYANDREAD_TIME)
        yield simpy.events.AllOf(e, [e.process(self.run_scheduler(e))])

        yield e.timeout(float('inf'))

    def run_job(self, what, e):
        def processor():
            if False:
                yield e.timeout(0)

            from_gh = pgh.encode(latitude=42.6, longitude=-5.6, precision=7)
            to_gh = pgh.encode(latitude=42.5, longitude=-5.7, precision=7)
            self.topic_id = uuid4()
            topic = RequestPayload(self.topic_id,
                                   TaxiTopic(from_geohash=from_gh,
                                             to_geohash=to_gh,
                                             pickup_after=datetime.now(),
                                             dropoff_before=datetime.now() + timedelta(minutes=20)),
                                   self.certificate)
            topic.sign(self._private_key)
            self.broadcast(e, topic)
            return None,

        self.trace(e, "run_job", what)
        return e.process(processor())

    def payandread_job(self, what, e):
        def processor():
            if False:
                yield e.timeout(0)

            responses = self.get_responses(e, self.topic_id)
            print(responses)
            reply_payload, network_invoice = responses[0][0]
            self.pay_and_read_response(e, reply_payload, network_invoice)
            return None,

        self.trace(e, "pay&read", what)
        return e.process(processor())

    def on_return(self, e, val):
        self.trace(e, val)


def main(sim_id):

    with Stopwatch() as sw:
        def printMessages(msgs):
            for m in msgs:
                print(m)

        ca = create_certification_authority("CA")
        ca_certificate = ca.issue_certificate(
            ca.ca_public_key, "is_ok", True,
            not_valid_after=datetime.now()+timedelta(days=7),
            not_valid_before=datetime.now()-timedelta(days=7))
        settler = Settler(
            ca_certificate,
            ca._ca_private_key,
            PaymentChannel(),
            price_amount_for_settlement=12)

        things = dict()

        things["GigWorker1"] = GigWorker("GigWorker1", ca, 1, settler)
        things["Customer1"] = Customer("Customer1", ca, 1, settler)

        things["GigWorker1"].connect_to(things["Customer1"])

        print(things)

        simulate(sim_id, things, until=float('inf'))

        for a in things:
            if (len(things[a].queue.items) > 0):
                print(a)
                printMessages(things[a].queue.items)

    print(sw.total)


main(sim_id="")
