from typing import Tuple

import crypto
from myrepr import ReprObject
from datetime import datetime


class Certificate(ReprObject):
    def __init__(self, public_key: bytes,
                 name: str,
                 value,
                 not_valid_after: datetime,
                 not_valid_before: datetime, signature) -> None:
        self.public_key = public_key
        self.name = name
        self.value = value
        self.not_valid_after = not_valid_after
        self.not_valid_before = not_valid_before
        self.signature = signature


class CertificationAuthority(ReprObject):
    def __init__(self, ca_private_key: bytes) -> None:
        self._ca_private_key = ca_private_key

    def issue_certificate(self, public_key: bytes,
                          name: str,
                          value,
                          not_valid_after: datetime,
                          not_valid_before: datetime) -> Certificate:
        obj = (public_key, name, value, not_valid_after, not_valid_before)
        signature = crypto.sign_object(obj, self._ca_private_key)
        return Certificate(public_key, name, value, not_valid_after, not_valid_before, signature)

    def is_revoked(certificate: Certificate) -> bool:
        return False


def verify_certificate(ca_public_key: bytes, certificate: Certificate) -> bool:
    obj = (certificate.public_key, certificate.name, certificate.value,
           certificate.not_valid_after, certificate.not_valid_before)
    signature = certificate.signature
    return crypto.verify_object(obj, signature, ca_public_key)


def create_certification_authority() -> Tuple[CertificationAuthority, bytes]:
    ca_private_key, ca_public_key = crypto.create_keys()
    return CertificationAuthority(ca_private_key), ca_public_key
