from typing import Tuple
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import serialization, hashes
from cryptography.fernet import Fernet
import pickle


def create_keys() -> Tuple[bytes, bytes]:
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
