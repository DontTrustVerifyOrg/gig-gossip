from collections import namedtuple
import numpy as np
import sys

ScheduleJob = namedtuple('ScheduleJob', 'job on_return')
ScheduleItem = namedtuple(
    'ScheduleItem', 'event start_at repeat_every num_repeats')


class Scheduler:
    def __init__(self):
        self._event_list = []

    def schedule(self, job, on_return, start_at, repeat_every=None, num_repeats=None):
        self._event_list.append(ScheduleItem(ScheduleJob(
            job, on_return), start_at, repeat_every, num_repeats))

    def _schedule_event_generator(self):
        cur = -1
        while (True):
            oneshot = [
                m for m in self._event_list if m.repeat_every is None and m.start_at > cur]
            cyclic = [m for m in self._event_list if m.repeat_every is not None and (
                m.num_repeats is None or cur < m.start_at+m.repeat_every*(m.num_repeats-1))]
            if not oneshot and not cyclic:
                break

            if cyclic:
                sched = np.array([m.start_at for m in cyclic])
                perio = np.array([m.repeat_every for m in cyclic])
                nexts = np.maximum(np.floor(
                    (cur - np.array(sched)) / np.array(perio)).astype(int)+1, 0) * perio + sched
                mincyc = np.min(nexts)
                mincyci = np.where(nexts == mincyc)[0]
            else:
                mincyc = sys.maxsize

            if oneshot:
                nexts = [m.start_at for m in oneshot]
                minosh = np.min(nexts)
                minoshi = np.where(nexts == minosh)[0]
            else:
                minosh = sys.maxsize

            if cur < 0:
                cur = 0

            if mincyc < minosh:
                yield mincyc-cur, [cyclic[m].event for m in mincyci]
                cur = mincyc
            elif minosh < mincyc:
                yield minosh-cur, [oneshot[m].event for m in minoshi]
                cur = minosh
            else:
                eve = [cyclic[m].event for m in mincyci]
                eve.extend([oneshot[m].event for m in minoshi])
                yield minosh-cur, eve
                cur = minosh


def test_scheduler():

    sched = Scheduler()
    sched.schedule("0:_:_", "", 0)
    sched.schedule("1:5:_", "", 1, 5)
    sched.schedule("3:2:_", "", 3, 2)
    sched.schedule("6:_:_", "", 6)
    sched.schedule("10:3:2", "", 10, 3, 2)
    sched.schedule("1:5:0", "", 1, 5, 0)
    sched.schedule("2:5:1", "", 2, 5, 1)
    sched.schedule("3:5:1", "", 3, 5, 1)
    sched.schedule("4:5:1", "", 4, 5, 1)
    sched.schedule("5:5:2", "", 5, 5, 2)
    sched.schedule("6:5:1", "", 6, 5, 1)
    l = []
    ct = 0
    for t, events in sched._schedule_event_generator():
        ct += t
        l.append([ct, [m.job for m in events]])
        if ct >= 100:
            break

    expect = eval(
        "[[0, ['0:_:_']], [1, ['1:5:_']], [2, ['2:5:1']], [3, ['3:2:_', '3:5:1']], [4, ['4:5:1']], [5, ['3:2:_', '5:5:2']], [6, ['1:5:_', '6:5:1', '6:_:_']], [7, ['3:2:_']], [9, ['3:2:_']], [10, ['10:3:2', '5:5:2']], [11, ['1:5:_', '3:2:_']], [13, ['3:2:_', '10:3:2']], [15, ['3:2:_']], [16, ['1:5:_']], [17, ['3:2:_']], [19, ['3:2:_']], [21, ['1:5:_', '3:2:_']], [23, ['3:2:_']], [25, ['3:2:_']], [26, ['1:5:_']], [27, ['3:2:_']], [29, ['3:2:_']], [31, ['1:5:_', '3:2:_']], [33, ['3:2:_']], [35, ['3:2:_']], [36, ['1:5:_']], [37, ['3:2:_']], [39, ['3:2:_']], [41, ['1:5:_', '3:2:_']], [43, ['3:2:_']], [45, ['3:2:_']], [46, ['1:5:_']], [47, ['3:2:_']], [49, ['3:2:_']], [51, ['1:5:_', '3:2:_']], [53, ['3:2:_']], [55, ['3:2:_']], [56, ['1:5:_']], [57, ['3:2:_']], [59, ['3:2:_']], [61, ['1:5:_', '3:2:_']], [63, ['3:2:_']], [65, ['3:2:_']], [66, ['1:5:_']], [67, ['3:2:_']], [69, ['3:2:_']], [71, ['1:5:_', '3:2:_']], [73, ['3:2:_']], [75, ['3:2:_']], [76, ['1:5:_']], [77, ['3:2:_']], [79, ['3:2:_']], [81, ['1:5:_', '3:2:_']], [83, ['3:2:_']], [85, ['3:2:_']], [86, ['1:5:_']], [87, ['3:2:_']], [89, ['3:2:_']], [91, ['1:5:_', '3:2:_']], [93, ['3:2:_']], [95, ['3:2:_']], [96, ['1:5:_']], [97, ['3:2:_']], [99, ['3:2:_']], [101, ['1:5:_', '3:2:_']]]")
    assert (l == expect)
