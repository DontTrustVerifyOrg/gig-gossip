import inspect
import logging
import operator
import random
import sys
import uuid
from collections import namedtuple
from copy import deepcopy
from functools import reduce
from itertools import groupby

import numpy as np
import simpy
from scheduler import Scheduler
from units import minute


class bcolors:
    DEFAULT = '\x1b[0m'
    WHITE = '\x1b[0m'
    RED = '\x1b[31m'
    GREEN = '\x1b[32m'
    YELLOW = '\x1b[33m'
    BLUE = '\x1b[34m'
    GRAY = '\x1b[90m'
    MAGENTA = '\x1b[35m'
    CYAN = '\x1b[36m'
    BOLD = '\x1b[1m'
    UNDERLINE = '\x1b[4m'


def simulation_trace(env, color: bcolors, name, *argv):
    """Main debugging tool. It generates a trace 

    Args:
        env: the simpy environment
        *argv: print arguments

    Returns:
        nothing, it just prints the trace
    """
    n = env.now
    sid = env.sim_id
    d = int(n/(24*60))
    h = int((n - d*24*60)/60)
    m = int(n - d*24*60 - h*60)
    dow = [bcolors.RED+'Sun', bcolors.BLUE+'Mon', bcolors.CYAN+'Tue', bcolors.BLUE +
           'Wed', bcolors.CYAN+'Thu', bcolors.BLUE+'Fri', bcolors.RED+'Sat'][d % 7]
    row = [sid + (" : " if sid != "" else "") + dow, str(d)+" " +
           str(h).zfill(2)+":"+str(m).zfill(2)+bcolors.WHITE+"|"+str(name)+">" + color, *argv, bcolors.DEFAULT]
    print(*row)
    if env.history is not None:
        env.history.append(row)


class Thing:
    """The base class for an `Agent` class and for a `Broadcaster` class."""

    def __init__(self, name):
        self.name = name

    def create_queue(self, env):
        self.queue = simpy.Store(env)

    def trace(self, env, *args):
        simulation_trace(env, bcolors.GRAY, self.name, *args)

    def info(self, env, *args):
        simulation_trace(env, bcolors.YELLOW, self.name, *args)

    def error(self, env, *args):
        simulation_trace(env, bcolors.RED, self.name, *args)

    def homeostasis(self, env):
        self.trace(env, "STARTS")
        yield env.timeout(float('inf'))


CollectingItem = namedtuple(
    'CollectingItem', 'sid collecting_condition collection')


class Agent(Thing, Scheduler):
    """This is an Agent class. Contains all the needed functionality making agents alive.
    """

    _sessionIDCnt = 0

    def __init__(self, name):
        """Constructor for the `Agent` class.

        Args:
            name (str): The name of the Agent for reporting purposes. It should be an unique identifier of the object.
        """
        Thing.__init__(self, name)
        Scheduler.__init__(self)
        self._collectors = {}

    def _prepare_for_response(self, env):
        sid = Agent._sessionIDCnt
        Agent._sessionIDCnt += 1

        store = simpy.Store(env, capacity=1)
        item = CollectingItem(sid, store, [])
        self._collectors[sid] = item
        return item

    def reply_and_wait(self, env, msg, data, timeout=None):
        """Reply on a `msg` message and therefore to conduct a dialog with other Agent.

        Args:
            env: The simpy environment.
            msg: The message that is currently being responded
            data (dict): a message content
            timeout (int): a timeout telling when to stop (or None if infinite)

        Returns:
            A reply to reply messages (None means that timeout was reached before the reply to this reply was delivered)
        """

        data = deepcopy(data)

        def generator():
            item = self._prepare_for_response(env)
            data['__sid__'] = item.sid
            rpl = msg.reply(env, data, -1)
            rpl.target.queue.put(rpl)

            self.trace(env, "waits for ...")
            if (not timeout is None):
                yield env.timeout(timeout/minute)

            del self._collectors[item.sid]

            return item.collection[0] if item.collection else None

        return env.process(generator())

    def reply(self, env, msg, data):
        """Reply on a `msg` message and therefore to conduct a dialog with other Agent.

        Args:
            env: The simpy environment.
            msg: The message that is currently being responded
            data (dict): a message content
        Returns:
            Nothing
        """
        data = deepcopy(data)

        rpl = msg.reply(env, data)
        rpl.target.queue.put(rpl)

    def new_message(self, env, target, data):
        """Send a new message to another Agent

        Args:
            env: The simpy environment.
            target(agent): The target of the message
            data (dict): A message content
        Returns:
            Nothing
        """
        data = deepcopy(data)

        msg = DirectMessage(sender=self, target=target, data=data)
        target.queue.put(msg)

    def new_message_and_wait(self, env, target, data, timeout=None):
        """Send a new message to another Agent

        Args:
            env: The simpy environment.
            target(agent): The target of the message
            data (dict): A message content
            timeout (int): a timeout telling when to stop (or None if infinite)
        Returns:
            A reply to message (None means that timeout was reached before the reply was delivered)
        """

        data = deepcopy(data)

        def generator():
            msg = DirectMessage(sender=self, target=target, data=data)
            target.queue.put(msg)

            item = self._prepare_for_response(env)

            self.trace(env, "waits for ...")
            if (not timeout is None):
                yield env.timeout(timeout/minute)
            del self._collectors[item.sid]

            return item.collection[0] if item.collection else None

        return env.process(generator())

    def start_state(self, env, m):
        self.trace(env, "received a request ", m.data, "from", m.sender)
        if (m.data is not None):
            if (inspect.isgeneratorfunction(self.on_message)):
                env.process(self.on_message(env, m))
            else:
                self.on_message(env, m)

    def on_message(self, env, m):
        self.trace(env, "unknown request:", m)

    def run_scheduler(self, e):
        oldcur = e.now
        for t, events in self._schedule_event_generator():
            dt = (e.now - oldcur)
            yield e.timeout(max(t - dt, 0))
            oldcur = e.now
            res = yield simpy.events.AllOf(e, [x.job(e) for x in events])
            for r, x in zip(res, events):
                x.on_return(*r.value)

    def __repr__(self):
        return self.__str__()

    def __str__(self):
        return f"{self.__class__.__name__}({self.name})"


class DirectMessage:
    """The message class.
    """

    def __init__(self, sender, target, data, id=None):
        self.sender = sender
        self.target = target
        self.data = data
        self.id = uuid.uuid4() if id is None else id

    def reply(self, env, data):
        """The method that creates the reply message for `self`"""
        return DirectMessage(sender=self.target, target=self.sender, data=data, id=None)

    def forward(self, env, target, id=None):
        """The method that creates the forward message for `self`"""
        return DirectMessage(sender=self.sender, target=target, data=self.data, id=id)

    def __repr__(self):
        return self.__str__()

    def __str__(self):
        return "{" + str(self.id) + "} " + str(self.sender) + " >--[ " + (
            ("DATA:" + str(self.data)) if not self.data is None else "") + " ]--> " + str(self.target)


def _message_loop(target, env, message_flow_in_trace):
    while (True):
        message = yield target.queue.get()

        if message_flow_in_trace:
            simulation_trace(env, bcolors.GREEN, "", message.sender, ">--[",
                             ("DATA:" + str(message.data)
                              ) if not message.data is None else "",
                             "]-->",
                             message.target)

        message.target.start_state(env, message)


def simulate(sim_id, things, until=None, history=None, message_flow_in_trace=True):
    """The simulation entry message

    Args:
        msgs (list of messages): the initial list of messages
        things (list of things): the initial list of things (agents and broadcasters)
        until (int): simulation time (None - forever)
    """

    env = simpy.Environment()
    env.sim_id = sim_id
    env.things = things
    env.history = history

    for k, t in things.items():
        t.create_queue(env)
        env.process(t.homeostasis(env))

    until = float('inf') if until is None else until
    lastnow = 0

    while True:
        for k, t in things.items():
            env.process(_message_loop(t, env, message_flow_in_trace))

        while env.peek() < until:
            lastnow = env.now
            env.step()

        if not any(t.queue.items for t in things.values()):
            break

        env._now = lastnow
