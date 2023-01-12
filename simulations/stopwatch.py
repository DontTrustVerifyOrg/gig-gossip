from timeit import default_timer as timer

class Stopwatch(object):
    def __init__(self):
        self.start_time = None
        self.stop_time = None

    def start(self):
        self.start_time = timer()
    
    def reset(self):
        self.start_time = timer()

    def stop(self):
        self.stop_time = timer()

    @property
    def elapsed(self):
        return timer() - self.start_time if self.start_time is not None else 0

    @property
    def total(self):
        return self.stop_time - self.start_time

    def __enter__(self):
        self.start()
        return self

    def __exit__(self, type, value, traceback):
        self.stop()