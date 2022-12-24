from datetime import datetime
from myrepr import ReprObject


class Invoice:
    pass


class ProofOfPayment(ReprObject):
    def __init__(self, invoice: Invoice) -> None:
        self.invoice = invoice

    def validate(self) -> bool:
        return self.invoice.is_paid()


class Invoice(ReprObject):
    def __init__(self, account: str, amount: int, valid_till: datetime) -> None:
        self.account = account
        self.amount = amount
        self.valid_till = valid_till
        self._is_paid = False

    def pay(self, account: str) -> ProofOfPayment:
        self._is_paid = True
        return ProofOfPayment(self)

    def is_paid(self) -> bool:
        return self._is_paid


class PaymentChannel(ReprObject):
    def __init__(self, account: str) -> None:
        self.account = account

    def create_invoice(self, amount: int, valid_till: datetime) -> Invoice:
        return Invoice(self.account, amount, valid_till)
