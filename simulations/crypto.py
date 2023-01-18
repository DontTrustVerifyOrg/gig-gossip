from typing import Tuple, List
from cryptography.hazmat.primitives.asymmetric import rsa, padding, utils
from cryptography.hazmat.primitives import serialization, hashes
from cryptography.exceptions import InvalidSignature
from cryptography.fernet import Fernet
import pickle


def generate_asymetric_keys() -> Tuple[bytes, bytes]:
    priv_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )
    private_key = priv_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=serialization.NoEncryption())

    pub_key = priv_key.public_key()
    public_key = pub_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo)

    return private_key, public_key


def encrypt_object(obj, public_key: bytes) -> bytes:
    pub_key = serialization.load_pem_public_key(
        public_key
    )
    bobj = pickle.dumps(obj)
    key = Fernet.generate_key()
    ebobj = Fernet(key).encrypt(bobj)
    ekey = pub_key.encrypt(key,
                           padding.OAEP(
                               mgf=padding.MGF1(algorithm=hashes.SHA256()),
                               algorithm=hashes.SHA256(),
                               label=None
                           ))
    return pickle.dumps((ekey, ebobj))


def decrypt_object(blob: bytes, private_key: bytes):
    priv_key = serialization.load_pem_private_key(
        private_key,
        password=None,
    )
    ekey, ebobj = pickle.loads(blob)
    key = priv_key.decrypt(ekey,
                           padding.OAEP(
                               mgf=padding.MGF1(algorithm=hashes.SHA256()),
                               algorithm=hashes.SHA256(),
                               label=None
                           ))
    bobj = Fernet(key).decrypt(ebobj)
    return pickle.loads(bobj)


def sign_object(obj, private_key: bytes) -> bytes:
    priv_key = serialization.load_pem_private_key(
        private_key,
        password=None,
    )
    bobj = pickle.dumps(obj)
    chosen_hash = hashes.SHA256()
    hasher = hashes.Hash(chosen_hash)
    hasher.update(bobj)
    return priv_key.sign(
        hasher.finalize(),
        padding.PSS(
            mgf=padding.MGF1(hashes.SHA256()),
            salt_length=padding.PSS.MAX_LENGTH
        ),
        utils.Prehashed(chosen_hash)
    )


def verify_object(obj, signature: bytes, public_key: bytes) -> bool:
    pub_key = serialization.load_pem_public_key(
        public_key
    )
    bobj = pickle.dumps(obj)
    chosen_hash = hashes.SHA256()
    hasher = hashes.Hash(chosen_hash)
    hasher.update(bobj)
    try:
        pub_key.verify(
            signature,
            hasher.finalize(),
            padding.PSS(
                mgf=padding.MGF1(hashes.SHA256()),
                salt_length=padding.PSS.MAX_LENGTH
            ),
            utils.Prehashed(chosen_hash)
        )
        return True
    except InvalidSignature:
        return False


def _compute_hash(items: list, chosen_hash) -> bytes:
    hasher = hashes.Hash(chosen_hash)
    for l in items:
        hasher.update(l)
    return hasher.finalize()


def compute_sha256(items: list) -> bytes:
    return _compute_hash(items, hashes.SHA256())


def compute_sha512(items: list) -> bytes:
    return _compute_hash(items, hashes.SHA512())


def generate_symmetric_key() -> bytes:
    return Fernet.generate_key()


def symmetric_encrypt(key: bytes, obj) -> bytes:
    bobj = pickle.dumps(obj)
    return Fernet(key).encrypt(bobj)


def symmetric_decrypt(key: bytes, ebobj: bytes):
    bobj = Fernet(key).decrypt(ebobj)
    return pickle.loads(bobj)
