import simpy
import inspect

from collections import namedtuple
import numpy as np
import sys
import operator
import random
from scheduler import Scheduler

from functools import reduce
from itertools import groupby

from units import minute

import uuid

class bcolors:
    DEFAULT = '\x1b[0m'
    RED = '\x1b[31m'
    GREEN = '\x1b[32m'
    YELLOW = '\x1b[33m'
    BLUE = '\x1b[34m'
    MAGENTA = '\x1b[35m'
    CYAN = '\x1b[36m'
    BOLD = '\x1b[1m'
    UNDERLINE = '\x1b[4m'


def ctx(env, context, trs):
    if (context in env.verbose):
        trs()

def trace(env, *argv):
    """Main debugging tool. It generates a trace for `env` in specific `context`. Set of visible `context`s can be selected then via verbose argument of the `simulate` function.

    Note:
        If env is None and context="?" the function prints all the `context` strings used during the simulation.

    Args:
        env: the simpy environment
        context: string that describes the verbosity context

    Returns:
        nothing, it just prints the trace
    """
    n=env.now
    sid = env.sim_id
    d = int(n/(24*60))
    h = int((n - d*24*60)/60)
    m = int(n - d*24*60 - h*60)
    dow = [bcolors.RED+'Sun',bcolors.BLUE+'Mon',bcolors.CYAN+'Tue',bcolors.BLUE+'Wed',bcolors.CYAN+'Thu',bcolors.BLUE+'Fri',bcolors.YELLOW+'Sat'][d%7]
    print(sid +(" : " if sid!="" else ""),")",dow,str(d)+" "+str(h).zfill(2)+":"+str(m).zfill(2), bcolors.DEFAULT , "|", *argv)


class Thing:
    """The base class for an `Agent` class and for a `Broadcaster` class."""

    def __init__(self, context_name, name):
        self.context_name = context_name
        self.name = name

    def create_queue(self, env):
        self.queue = simpy.Store(env)

    def ctx(self,env,trs, verb_pfx=None):
        ctx(env, self.context_name if verb_pfx is None else self.context_name+verb_pfx,trs)

    def trace(self,env,*args):
        trace(env,self.name, *args)

    def homeostasis(self,env):
        self.ctx(env,lambda:self.trace(env,"STARTS"))
        yield env.timeout(float('inf'))

CollectingItem = namedtuple('CollectingItem', 'sid collecting_condition collection')

class Agent(Thing,Scheduler):
    """This is an Agent class. Contains all the needed functionality making agents alive.
    """

    _sessionIDCnt = 0

    def __init__(self, context_name, name):
        """Constructor for the `Agent` class.

        Args:
            name (str): The name of the Agent for reporting purposes. It should be an unique identifier of the object.
        """
        Thing.__init__(self,context_name, name)
        Scheduler.__init__(self)
        self._collectors = {}

    def _prepare_for_response(self, env):
        sid = Agent._sessionIDCnt
        Agent._sessionIDCnt+=1

        store = simpy.Store(env, capacity=1)
        item = CollectingItem(sid, store, [])
        self._collectors[sid]= item
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

        def generator():
            item = self._prepare_for_response(env)
            data['__sid__']=item.sid
            rpl = msg.reply(env, data, -1)
            rpl.target.queue.put(rpl)

            self.ctx(env,lambda:self.trace(env, "waits for ..."))
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

        def generator():
            msg = DirectMessage(sender=self, target=target, data=data)
            target.queue.put(msg)
            
            item = self._prepare_for_response(env)

            self.ctx(env,lambda:self.trace(env, "waits for ..."))
            if (not timeout is None):
                yield env.timeout(timeout/minute) 
            del self._collectors[item.sid]

            return item.collection[0] if item.collection else None

        return env.process(generator())
        
    def start_state(self, env, m):
        self.ctx(env,lambda:self.trace(env, "received a request ", m.data, "from", m.sender)," rec")
        if (m.data is not None):
            if (inspect.isgeneratorfunction(self.on_message)):
                env.process(self.on_message(env, m))
            else:
                self.on_message(env, m)

    def on_message(self, e, m):
        self.ctx(e, lambda: self.trace(e, "unknown request:", m))

    def run_scheduler(self,e):
        oldcur = e.now
        for t, events in self._schedule_event_generator():
            dt = (e.now - oldcur)
            yield e.timeout(max(t - dt, 0))
            oldcur = e.now
            res = yield simpy.events.AllOf(e, [x.job(e) for x in events])
            for r, x in zip(res, events):
                x.on_return(*r.value)
                
    def __str__(self):
        return self.name

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

    def forward(self,env, target, id=None):
        """The method that creates the forward message for `self`"""
        return DirectMessage(sender=self.sender, target=target  , data=self.data, id=id)

    def __repr__(self):
        return self.__str__()

    def __str__(self):
        return "{" + str(self.id) + "} " + str(self.sender) + " >--[ " + (
        ("DATA:" + str(self.data)) if not self.data is None else "") + " ]--> " + str(self.target)



def _message_loop(target,env):
    while(True):
        message = yield target.queue.get()

        ctx(env,"message flow", lambda:trace(env, bcolors.GREEN, " {", message.id, "} " , message.sender, ">--[",
              ("DATA:" + str(message.data)) if not message.data is None else "",
              "]-->",
              message.target, bcolors.DEFAULT))

        message.target.start_state(env, message)


def simulate(sim_id, things, until=None, verbose={}):
    """The simulation entry message

    Args:
        msgs (list of messages): the initial list of messages
        things (list of things): the initial list of things (agents and broadcasters)
        until (int): simulation time (None - forever)
        verbose(set(str)): set of context strings to show the `trace` from
    """

    env = simpy.Environment()
    env.verbose = verbose
    env.sim_id=sim_id
    env.things = things

    for k,t in things.items():
        t.create_queue(env)
        env.process(t.homeostasis(env))

    until = float('inf') if until is None else until
    lastnow = 0

    while True:
        for k,t in things.items():
            env.process(_message_loop(t,env))

        while env.peek() < until:
            lastnow = env.now
            env.step()

        if not any(t.queue.items for t in things.values()):
            break

        env._now = lastnow



def execute_simulation(main, sim_id):
    main(sim_id)
