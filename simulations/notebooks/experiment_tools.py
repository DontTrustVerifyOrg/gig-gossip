from run_tools import is_notebook
import sys
import numpy as np
import random
from datetime import datetime
import uuid
from mass_tools import time_to_int

if is_notebook():
    PARAM_ID = 295
else:
    PARAM_ID = int(sys.argv[-1])

RANDOM_SEED = 1234

random.seed(RANDOM_SEED)
np.random.seed(RANDOM_SEED)

FOLDNAME = f"sim/{PARAM_ID:08d}_"+(str(datetime.now())+" "+uuid.uuid4().hex).replace('-',
                                                                                     '').replace(' ', '_').replace(':', '').replace('.', '_')
RUN_START = time_to_int(1, 8, 0)
