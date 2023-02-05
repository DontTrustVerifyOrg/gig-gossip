# %%
from typing import Dict, Tuple
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
# %%

PAYANDREAD_TIME = time_to_int(2, 8, 0)


class GridNodeType(Enum):
    Gossiper = 0
    Customer = 1
    GigWorker = 2


class TaxiTopic(AbstractTopic):
    def __init__(self, from_geohash: str,  to_geohash: str, pickup_after: datetime, dropoff_before: datetime) -> None:
        self.from_geohash = from_geohash
        self.to_geohash = to_geohash
        self.pickup_after = pickup_after
        self.dropoff_before = dropoff_before


class GridNode(SweetGossipNode):
    def __init__(self, name,  ca: CertificationAuthority, price_amount_for_routing, settler: Settler):
        self.grid_node_type = GridNodeType.Gossiper
        private_key, public_key = crypto.generate_asymetric_keys()
        certificate = ca.issue_certificate(public_key, "is_ok", True, not_valid_after=datetime.now(
        )+timedelta(days=7), not_valid_before=datetime.now()-timedelta(days=7))
        payment_channel = PaymentChannel()
        super().__init__(name, certificate, private_key, payment_channel, price_amount_for_routing,
                         broadcast_conditions_timeout=timedelta(days=7), broadcast_conditions_pow_scheme="sha256", broadcast_conditions_pow_complexity=0, invoice_payment_timeout=timedelta(days=1),
                         timestamp_tolerance=timedelta(seconds=10),
                         settler=settler)

    def set_grid_node_type(self, grid_node_type: GridNodeType):
        self.grid_node_type = grid_node_type

    def accept_topic(self, topic: AbstractTopic) -> bool:
        if isinstance(topic, TaxiTopic):
            return len(topic.from_geohash) >= 7 and len(topic.to_geohash) >= 7 and datetime.now() <= topic.dropoff_before
        return False

    def accept_broadcast(self, signed_topic: RequestPayload) -> Tuple[bytes, int]:
        if self.grid_node_type == GridNodeType.GigWorker:
            return bytes(f"mynameis={self.name}", encoding="utf8"), 4321
        else:
            return None, 0

    def homeostasis(self, e):
        if self.grid_node_type == GridNodeType.Customer:
            self.info(e, "is starting...")

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
    history = list()
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

        things: Dict[str, GridNode] = dict()

        GRID_SHAPE = (10, 10)

        for nod_idx in itertools.product(*(range(s) for s in GRID_SHAPE)):
            node_name = f"GridNode<{nod_idx}>"
            things[node_name] = GridNode(node_name,
                                         ca,
                                         1,
                                         settler)
#            print(node_name, ":", things[node_name].payment_channel)

        already = set()
        for nod_idx in itertools.product(*(range(s) for s in GRID_SHAPE)):
            node_name = f"GridNode<{nod_idx}>"
            print("*", node_name)
            for k in range(len(nod_idx)):
                nod1_idx = tuple([(x+1) % GRID_SHAPE[k] if i ==
                                  k else x for i, x in enumerate(nod_idx)])
                node_name_1 = f"GridNode<{nod1_idx}>"
                if node_name+":"+node_name_1 in already:
                    continue
                if node_name_1+":"+node_name in already:
                    continue

                things[node_name].connect_to(things[node_name_1])
                already.add(node_name+":"+node_name_1)
                already.add(node_name_1+":"+node_name)

                print(node_name, "<->", node_name_1)

        things_list = list(things.values())

        for i in range(5):
            start_idx = random.randint(0, len(things_list)-1)
            end_idx = random.randint(0, len(things_list)-1)
            print(things_list[start_idx].name,
                  "->>>", things_list[end_idx].name)

            things_list[start_idx].set_grid_node_type(GridNodeType.Customer)
            things_list[end_idx].set_grid_node_type(GridNodeType.GigWorker)

        simulate(sim_id, things, until=float('inf'),
                 history=history, message_flow_in_trace=False)

        for a in things:
            if (len(things[a].queue.items) > 0):
                print(a)
                printMessages(things[a].queue.items)

    print(sw.total)
    return history


h = main(sim_id="")

# %%
