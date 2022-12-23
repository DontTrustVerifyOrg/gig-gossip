
from datetime import datetime


class Invoice:
    def __init__(self, account: str, amount: int, valid_till: datetime, message: str) -> None:
        self.account = account
        self.amount = amount
        self.valid_till = valid_till
        self.message = message

    def __repr__(self):
        return f"""Invoice(
        account={self.account},
        amount={self.amount},
        message={self.message},
        valid_till={self.valid_till})"""

    def __str__(self):
        return self.__repr__()


class ProofOfPayment:
    def __init__(self, message: str) -> None:
        self.message = message

    def __repr__(self):
        return f"""ProofOfPayment(
        message={self.message})"""

    def __str__(self):
        return self.__repr__()


class PaymentChannel:
    def __init__(self, account: str) -> None:
        self.account = account

    def create_invoice(self, amount: int, valid_till: datetime, message: str) -> Invoice:
        return Invoice(amount, valid_till, message)

    def pay_invoice(self, invoice: Invoice, message: str) -> ProofOfPayment:
        return ProofOfPayment(message)

    def __repr__(self):
        return f"""PaymentChannel(
        account={self.account})"""

    def __str__(self):
        return self.__repr__()


def is_invoice_paid(invoice: Invoice) -> bool:
    return False


def validate_proof_of_payment(invoice: Invoice, pop: ProofOfPayment) -> bool:
    return True
