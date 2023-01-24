from __future__ import annotations

from datetime import datetime
from myrepr import ReprObject
from crypto import compute_sha512, generate_symmetric_key
from collections.abc import Callable


def compute_payment_hash(preimage: bytes) -> bytes:
    return compute_sha512([preimage])


class HodlInvoice(ReprObject):
    def __init__(self, payment_hash: bytes, amount: int,
                 on_accepted: Callable[[HodlInvoice]],
                 valid_till: datetime,
                 ) -> None:
        self.payment_hash = payment_hash
        self.amount = amount
        self.valid_till = valid_till
        self.is_accepted = False
        self.is_settled = False
        self.on_accepted = on_accepted


class Invoice(ReprObject):
    def __init__(self, preimage: bytes, amount: int,
                 valid_till: datetime,
                 ) -> None:
        self._preimage = preimage
        self.payment_hash = compute_payment_hash(preimage)
        self.amount = amount
        self.valid_till = valid_till
        self.is_accepted = False


class PaymentChannel(ReprObject):
    def create_hodl_invoice(self, amount: int, payment_hash: bytes,
                            on_accepted: Callable[[HodlInvoice]],
                            valid_till: datetime = datetime.max,
                            ) -> HodlInvoice:
        return HodlInvoice(payment_hash, amount, on_accepted, valid_till)

    def create_invoice(self, amount: int, preimage: bytes,
                       valid_till: datetime = datetime.max,
                       ) -> Invoice:
        return Invoice(preimage, amount, valid_till)

    def pay_hodl_invoice(self, invoice: HodlInvoice, on_settled: Callable[[HodlInvoice, bytes]]) -> None:
        if invoice.is_accepted:
            return
        if datetime.now() > invoice.valid_till:
            return

        invoice.on_settled = on_settled
        invoice.is_accepted = True
        invoice.on_accepted(invoice)

    def settle_hodl_invoice(self, invoice: HodlInvoice, preimage: bytes) -> None:
        if invoice.is_settled:
            return
        if invoice.is_accepted:
            if compute_payment_hash(preimage) == invoice.payment_hash:
                invoice.preimage = preimage
                invoice.is_settled = True
                invoice.on_settled(invoice, preimage)
