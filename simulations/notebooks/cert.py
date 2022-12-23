import crypto


class Certificate:
    def __init__(self, public_key: bytes, properites: dict, signature) -> None:
        self.public_key = public_key
        self.properties = properites
        self.signature = signature

    def __repr__(self):
        return f"""Certificate(
        public_key={self.public_key},
        properties={self.properties},
        signature={self.signature})"""

    def __str__(self):
        return self.__repr__()


class CertificationAuthority:
    def __init__(self, public_key: bytes, private_key: bytes) -> None:
        self.public_key = public_key
        self._private_key = private_key

    def issue_certificate(self, public_key: bytes, properites: dict) -> Certificate:
        obj = (public_key, properites)
        signature = crypto.sign_object(obj, self._private_key)
        return Certificate(public_key, properites, signature)

    def __repr__(self):
        return f"""CertificationAuthority(
        public_key={self.public_key})"""

    def __str__(self):
        return self.__repr__()


def verify_certificate(ca_public_key: bytes, certificate: Certificate) -> bool:
    obj = (certificate.public_key, certificate.properites)
    signature = certificate.signature
    return crypto.verify_object(obj, signature, ca_public_key)
