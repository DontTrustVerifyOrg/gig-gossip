from mass import execute_simulation, simulate, trace
from mass_tools import time_to_int
from experiment_tools import FOLDNAME, RUN_START

from stopwatch import Stopwatch
from datetime import datetime, timedelta
from cert import CertificationAuthority
import crypto
from payments import PaymentChannel

from uuid import uuid4
from sweetgossip import SweetGossipNode, Topic
from functools import partial
import simpy


class Gossiper(SweetGossipNode):
    def __init__(self, context_name, name, ca: CertificationAuthority, price_amount_for_routing):
        private_key, public_key = crypto.create_keys()
        certificate = ca.issue_certificate(public_key, "is_ok", True, not_valid_after=datetime.now(
        )+timedelta(days=7), not_valid_before=datetime.now()-timedelta(days=7))
        account = uuid4().bytes
        payment_channel = PaymentChannel(account)
        super().__init__(context_name, name, certificate, private_key, payment_channel, price_amount_for_routing,
                         broadcast_conditions_timeout=timedelta(days=7), broadcast_conditions_pow_scheme="sha256", broadcast_conditions_pow_complexity=1, invoice_payment_timeout=timedelta(days=1))


class GigWorker(Gossiper):
    def accept_broadcast(self, signed_topic: Topic) -> bytes:
        return bytes(f"mynameis={self.name}", encoding="utf8")


class Customer(Gossiper):

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
            topic = Topic(uuid4(), "Test Topic", "/a/b/c", datetime.now() +
                          timedelta(days=10), datetime.now() -
                          timedelta(days=10),
                          self.certificate)
            topic.sign(self._private_key)
            self.broadcast(e, topic)
            return None,

        self.ctx(e, lambda: self.trace(e, what))
        self.log_history(e, "run_job", what)
        return e.process(processor())

    def on_return(self, e, val):
        self.ctx(e, lambda: self.trace(e, val))


def main(sim_id):
    with Stopwatch() as sw:
        def printMessages(msgs):
            for m in msgs:
                print(m)

        ca_private_key, ca_public_key = crypto.create_keys()
        ca = CertificationAuthority("CA", ca_private_key, ca_public_key)
        things = dict()

        things["GigWorker1"] = GigWorker("GigWorkers", "GigWorker1", ca, 1)
        things["Customer1"] = Customer("Customers", "Customer1", ca, 1)

        things["GigWorker1"].connect_to(things["Customer1"])

        print(things)

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

    print(sw.total)


execute_simulation(main, sim_id=FOLDNAME)
