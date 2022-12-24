import crypto
import pickle
import sys

from myrepr import ReprObject

MAX_POW_TARGET_SHA256 = int.from_bytes(
    b'\x0F\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF', 'big')


def _validate_sha25_pow(buf: bytes, nuance: int, pow_target: int) -> bool:
    return int.from_bytes(crypto.compute_sha256([
        buf,
        nuance.to_bytes(4, "big")
    ]), "big") <= pow_target


def validate_pow(obj, nuance: int, pow_scheme: str, pow_target: int) -> bool:
    if pow_scheme.lower() == "sha256":
        buf = bytearray(pickle.dumps(obj))
        return _validate_sha25_pow(buf, nuance, pow_target)
    return False


def compute_pow(obj, pow_scheme: str, pow_target: int) -> int:
    if pow_scheme.lower() == "sha256":
        buf = bytearray(pickle.dumps(obj))
        for nuance in range(sys.maxsize):
            if _validate_sha25_pow(buf, nuance, pow_target):
                return nuance
    return None


class ProofOfWork(ReprObject):
    def __init__(self, pow_scheme: str, pow_target: int, nuance: int) -> None:
        self.pow_scheme = pow_scheme
        self.pow_target = pow_target
        self.nuance = nuance

    def validate(self, obj) -> bool:
        row = (obj, self.pow_scheme, self.pow_target)
        return validate_pow(row, self.nuance, self.pow_scheme, self.pow_target)


class WorkRequest(ReprObject):
    def __init__(self, pow_scheme: str, pow_target: int) -> None:
        self.pow_scheme = pow_scheme
        self.pow_target = pow_target

    def compute_proof(self, obj) -> ProofOfWork:
        row = (obj, self.pow_scheme, self.pow_target)
        return ProofOfWork(
            self.pow_scheme, self.pow_target,
            compute_pow(row, self.pow_scheme, self.pow_target))
