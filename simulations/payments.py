from __future__ import annotations

from datetime import datetime
from myrepr import ReprObject
from crypto import compute_sha512, generate_symmetric_key


def compute_payment_hash(preimage: bytes) -> bytes:
    return compute_sha512([preimage])


class Invoice(ReprObject):
    def __init__(self, account: bytes, preimage: bytes, amount: int, valid_till: datetime) -> None:
        self.account = account
        self._preimage = preimage
        self.preimage = None
        self.payment_hash = compute_payment_hash(preimage)
        self.amount = amount
        self.valid_till = valid_till
        self.is_paid = False


class ProofOfPayment(ReprObject):
    def __init__(self, preimage: bytes) -> None:
        self.preimage = preimage

    def validate(self, invoice: Invoice) -> bool:
        return compute_payment_hash(self.preimage) == invoice.payment_hash


class PaymentChannel(ReprObject):
    def __init__(self, account: bytes) -> None:
        self.account = account

    def create_invoice(self, amount: int, preimage: bytes = None, valid_till: datetime = None) -> Invoice:
        if preimage is None:
            preimage = generate_symmetric_key()
        if valid_till is None:
            valid_till = datetime.max
        return Invoice(self.account, preimage, amount,  valid_till)

    def pay_invoice(self, invoice: Invoice) -> ProofOfPayment:
        if invoice.is_paid:
            return None
        if datetime.now() > invoice.valid_till:
            return None

        invoice.is_paid = True
        invoice.preimage = invoice._preimage
        return ProofOfPayment(invoice.preimage)
