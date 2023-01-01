class ReprObject:
    def __repr__(self):
        head = f"{self.__class__.__name__}"
        items = [f"{k}={v}" for k, v in vars(self).items() if k[0] != '_']
        if len(items) == 0:
            return head+"()\n"
        elif len(items) == 1:
            return head+"("+items[0]+")\n"
        else:
            return head+"(\n"+",\n".join(items[:-1])+",\n"+items[-1]+"\n)#"+head+"\n"

    def __str__(self):
        return self.__repr__()
