# %%
from typing import Dict
from mass import simulate
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
import itertools

from enum import Enum
import logging
# %%


class GridNodeType(Enum):
    Gossiper = 0
    Customer = 1
    GigWorker = 2


class GridNode(SweetGossipNode):
    def __init__(self, name,  ca: CertificationAuthority, price_amount_for_routing):
        self.grid_node_type = GridNodeType.Gossiper
        private_key, public_key = crypto.create_keys()
        certificate = ca.issue_certificate(public_key, "is_ok", True, not_valid_after=datetime.now(
        )+timedelta(days=7), not_valid_before=datetime.now()-timedelta(days=7))
        account = uuid4().bytes
        payment_channel = PaymentChannel(account)
        super().__init__(name, certificate, private_key, payment_channel, price_amount_for_routing,
                         broadcast_conditions_timeout=timedelta(days=7), broadcast_conditions_pow_scheme="sha256", broadcast_conditions_pow_complexity=0, invoice_payment_timeout=timedelta(days=1))

    def set_grid_node_type(self, grid_node_type: GridNodeType):
        self.grid_node_type = grid_node_type

    def accept_broadcast(self, signed_topic: Topic) -> bytes:
        if self.grid_node_type == GridNodeType.GigWorker:
            return bytes(f"mynameis={self.name}", encoding="utf8")
        else:
            return None

    def homeostasis(self, e):
        if self.grid_node_type == GridNodeType.Customer:
            self.info(e, "is starting...")

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

        self.trace(e, "run_job", what)
        return e.process(processor())

    def on_return(self, e, val):
        self.trace(e, val)


def main(sim_id):
    history = list()
    with Stopwatch() as sw:
        def printMessages(msgs):
            for m in msgs:
                print(m)

        ca_private_key, ca_public_key = crypto.create_keys()
        ca = CertificationAuthority("CA", ca_private_key, ca_public_key)
        things: Dict[str, GridNode] = dict()

        GRID_SHAPE = (6, 5,4)

        for nod_idx in itertools.product(*(range(s) for s in GRID_SHAPE)):
            node_name = f"GridNode<{nod_idx}>"
            things[node_name] = GridNode(node_name,
                                         ca,
                                         1)
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
#                nod2_idx = tuple([(x-1) % GRID_SHAPE[k] if i ==
#                                  k else x for i, x in enumerate(nod_idx)])
#                node_name_2 = f"GridNode<{nod2_idx}>"
#                things[node_name].connect_to(things[node_name_2])

                print(node_name, "<->", node_name_1)
 #               print(node_name,"<->",node_name_2)

        things_list = list(things.values())

        things_list[0].set_grid_node_type(GridNodeType.Customer)
        things_list[-1].set_grid_node_type(GridNodeType.GigWorker)

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
