from typing import Tuple, Dict

import crypto
from myrepr import ReprObject
from datetime import datetime


class CertificationAuthority:
    pass


CA_BY_NAME: Dict[str, CertificationAuthority] = dict()


class Certificate(ReprObject):
    def __init__(self, ca_name: str, public_key: bytes,
                 name: str,
                 value,
                 not_valid_after: datetime,
                 not_valid_before: datetime, signature) -> None:
        self.ca_name = ca_name
        self.public_key = public_key
        self.name = name
        self.value = value
        self.not_valid_after = not_valid_after
        self.not_valid_before = not_valid_before
        self.signature = signature

    def verify(self):
        ca = get_certification_authority_by_name(self.ca_name)
        if not ca is None:
            if not ca.is_revoked(self):
                obj = (self.ca_name, self.public_key, self.name, self.value,
                       self.not_valid_after, self.not_valid_before)
                return crypto.verify_object(obj, self.signature, ca.public_key)
        return False


class CertificationAuthority(ReprObject):
    def __init__(self, ca_name: str, ca_private_key: bytes, ca_public_key: bytes) -> None:
        global CA_BY_NAME
        self.ca_name = ca_name
        self._ca_private_key = ca_private_key
        self.ca_public_key = ca_public_key
        CA_BY_NAME[ca_name] = self

    def issue_certificate(self, public_key: bytes,
                          name: str,
                          value,
                          not_valid_after: datetime,
                          not_valid_before: datetime) -> Certificate:
        obj = (self.ca_name, public_key, name, value,
               not_valid_after, not_valid_before)
        signature = crypto.sign_object(obj, self._ca_private_key)
        return Certificate(self.ca_name, public_key, name, value, not_valid_after, not_valid_before, signature)

    def is_revoked(certificate: Certificate) -> bool:
        return False


def create_certification_authority(ca_name: str) -> CertificationAuthority:
    ca_private_key, ca_public_key = crypto.create_keys()
    return CertificationAuthority(ca_name, ca_private_key, ca_public_key)


def get_certification_authority_by_name(ca_name: str) -> CertificationAuthority:
    global CA_BY_NAME
    if ca_name in CA_BY_NAME:
        return CA_BY_NAME[ca_name]
    return None
