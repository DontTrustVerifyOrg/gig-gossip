from __future__ import annotations

from datetime import datetime
from myrepr import ReprObject
from crypto import compute_sha512


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

    def create_invoice(self, amount: int, preimage: bytes, valid_till: datetime) -> Invoice:
        return Invoice(self.account, amount, preimage, valid_till)

    def pay_invoice(self, invoice: Invoice) -> ProofOfPayment:
        invoice.is_paid = True
        invoice.preimage = invoice._preimage
        return ProofOfPayment(invoice.preimage)
