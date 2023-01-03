import base64

def moval(v):
    if isinstance(v, bytes):
        enc=base64.b64encode(v).decode("utf-8") 
        return f"<{len(v)}:{enc[:8]+'...'+enc[-10:-2]}>"

    if isinstance(v, list):
        return "["+", ".join(moval(m).__repr__() for m in v)+"]"

    return v

CUR_INT = 0

class ReprObject:
    def __repr__(self):
        global CUR_INT
        try:
            spaces = ' '*CUR_INT
            CUR_INT+=1
            head = f"{self.__class__.__name__}"
            items = [f"{k}={moval(v)}" for k, v in vars(
                self).items() if k[0] != '_']
            if len(items) == 0:
                return head+"()\n"+spaces
            elif len(items) == 1:
                return head+"("+items[0]+")\n"+spaces
            else:
                return head+"(\n"+spaces+(",\n"+spaces+" ").join(items[:-1])+",\n"+spaces+items[-1]+"\n"+spaces+")#"+head+"\n"+spaces
        finally:
            CUR_INT-=1

    def __str__(self):
        return self.__repr__()
