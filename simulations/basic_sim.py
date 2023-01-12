from mass import simulate, simulation_trace
from mass_tools import time_to_int
from experiment_tools import FOLDNAME, RUN_START
from myrepr import ReprObject

from stopwatch import Stopwatch
from datetime import datetime, timedelta
from cert import CertificationAuthority, create_certification_authority
import crypto
from payments import PaymentChannel

from uuid import uuid4
from sweetgossip import SweetGossipNode, RequestPayload, AbstractTopic
from functools import partial
import simpy

import pygeohash as pgh

class DriveTopic(ReprObject):
    def __init__(self, geohash: str, after: datetime, before: datetime) -> None:
        self.geohash = geohash
        self.after = after
        self.before = before

class Gossiper(SweetGossipNode):
    def __init__(self, name, ca: CertificationAuthority, price_amount_for_routing):
        private_key, public_key = crypto.create_keys()
        certificate = ca.issue_certificate(public_key, "is_ok", True, not_valid_after=datetime.now(
        )+timedelta(days=7), not_valid_before=datetime.now()-timedelta(days=7))
        account = uuid4().bytes
        payment_channel = PaymentChannel(account)
        super().__init__(name, certificate, private_key, payment_channel, price_amount_for_routing,
                         broadcast_conditions_timeout=timedelta(days=7), broadcast_conditions_pow_scheme="sha256", broadcast_conditions_pow_complexity=1, invoice_payment_timeout=timedelta(days=1))

    def accept_topic(self, topic: AbstractTopic) -> bool:
        if isinstance(topic, DriveTopic):
            return len(topic.geohash) >= 7 and datetime.now() <= topic.before
        return False

class GigWorker(Gossiper):
    def accept_broadcast(self, signed_topic: RequestPayload) -> bytes:
        return bytes(f"mynameis={self.name}", encoding="utf8")


class Customer(Gossiper):

    def homeostasis(self, e):
        self.trace(e, "is starting...")

        self.schedule(partial(self.run_job, {"run"}),
                      partial(self.on_return, e), RUN_START)

        yield simpy.events.AllOf(e, [e.process(self.run_scheduler(e))])

        yield e.timeout(float('inf'))

    def run_job(self, what, e):
        def processor():
            if False:
                yield e.timeout(0)

            gh = pgh.encode(latitude=42.6, longitude=-5.6, precision=7)
            topic = RequestPayload(uuid4(),
                                   DriveTopic(geohash=gh,
                                              after=datetime.now(),
                                              before=datetime.now() + timedelta(minutes=20)),
                          self.certificate)
            topic.sign(self._private_key)
            self.broadcast(e, topic)
            return None,

        self.trace(e, "run_job", what)
        return e.process(processor())

    def on_return(self, e, val):
        self.trace(e, val)


def main(sim_id):

    with Stopwatch() as sw:
        def printMessages(msgs):
            for m in msgs:
                print(m)

        ca = create_certification_authority("CA")

        things = dict()

        things["GigWorker1"] = GigWorker("GigWorker1", ca, 1)
        things["Customer1"] = Customer("Customer1", ca, 1)

        things["GigWorker1"].connect_to(things["Customer1"])

        print(things)

        simulate(sim_id, things, until=float('inf'))

        for a in things:
            if (len(things[a].queue.items) > 0):
                print(a)
                printMessages(things[a].queue.items)

    print(sw.total)


main(sim_id="")
