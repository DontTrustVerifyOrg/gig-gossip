import requests
from random import randbytes
import giggossipframes_pb2
import binascii
import hashlib
from schnorr_lib import schnorr_sign, schnorr_verify, pubkey_gen
from datetime import datetime
import uuid
import base64

class Wallet:
    def __init__(self, base_url, privkey,pubkey):
        self.base_url = base_url
        self.privkey = privkey
        self.pubkey = pubkey

    def get_token(self):
        api_url = f"{self.base_url}/gettoken?pubkey="+self.pubkey
        response = requests.get(api_url)
        response.raise_for_status()
        return uuid.UUID(response.json()["value"]).bytes

    def create_authtoken(self):
        token=self.get_token()
        authTok = giggossipframes_pb2.AuthToken()
        authTok.Header.PublicKey.Value = bytes.fromhex(self.pubkey)
        authTok.Header.Timestamp.Value = int(datetime.now().timestamp())
        authTok.Header.TokenId.Value = token
        authTok.Signature.Value = schnorr_sign(
            hashlib.sha256(authTok.Header.SerializeToString()).digest(),
            bytes.fromhex(self.privkey),randbytes(32))
        return base64.b64encode(authTok.SerializeToString())
    

    def topupandmine6blocks(self,bitcoinAddr,satoshis):
        api_url = f"{self.base_url}/topupandmine6blocks"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(), "bitcoinAddr":bitcoinAddr,"satoshis":satoshis})
        response.raise_for_status()
        return response.json()

    def sendtoaddress(self,bitcoinAddr,satoshis):
        api_url = f"{self.base_url}/sendtoaddress"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(), "bitcoinAddr":bitcoinAddr,"satoshis":satoshis})
        response.raise_for_status()
        return response.json()

    def generateblocks(self,blocknum):
        api_url = f"{self.base_url}/generateblocks"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(), "blocknum":blocknum})
        response.raise_for_status()
        return response.json()

    def newbitcoinaddress(self):
        api_url = f"{self.base_url}/newbitcoinaddress"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()

    def getbitcoinwalletballance(self,minConf):
        api_url = f"{self.base_url}/getbitcoinwalletballance"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(), "minConf":minConf})
        response.raise_for_status()
        return response.json()

    def getlndwalletballance(self):
        api_url = f"{self.base_url}/getlndwalletballance"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()

    def openreserve(self, satoshis):
        api_url = f"{self.base_url}/openreserve"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken() ,"satoshis":satoshis})
        response.raise_for_status()
        return response.json()

    def closereserve(self, reserveId):
        api_url = f"{self.base_url}/closereserve"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken() ,"reserveId":reserveId})
        response.raise_for_status()
        return response.json()

    def estimatefee(self,address,satoshis):
        api_url = f"{self.base_url}/estimatefee"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(), "address":address,"satoshis":satoshis})
        response.raise_for_status()
        return response.json()
    
    def getbalance(self):
        api_url = f"{self.base_url}/getbalance"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()
    
    def newaddress(self):
        api_url = f"{self.base_url}/newaddress"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()
    
    def registerpayout(self,satoshis,btcAddress,txfee):
        api_url = f"{self.base_url}/registerpayout"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"satoshis":satoshis,"btcAddress":btcAddress,"txfee":txfee})
        response.raise_for_status()
        return response.json()

    def addinvoice(self,satoshis,memo,expiry):
        api_url = f"{self.base_url}/addinvoice"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"satoshis":satoshis,"memo":memo,"expiry":expiry})
        response.raise_for_status()
        return response.json()

    def addhodlinvoice(self,satoshis,hashc,memo,expiry):
        api_url = f"{self.base_url}/addhodlinvoice"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"satoshis":satoshis,"hash":hashc, "memo":memo,"expiry":expiry})
        response.raise_for_status()
        print(response.json())
        return response.json()

    def decodeinvoice(self,paymentRequest):
        api_url = f"{self.base_url}/addhodlinvoice"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"paymentRequest":paymentRequest})
        response.raise_for_status()
        return response.json()

    def sendpayment(self,paymentRequest,timeout,feelimit):
        api_url = f"{self.base_url}/sendpayment"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"paymentRequest":paymentRequest,"timeout":timeout,"feelimit":feelimit})
        response.raise_for_status()
        return response.json()

    def estimateroutefee(self,paymentRequest):
        api_url = f"{self.base_url}/estimateroutefee"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"paymentRequest":paymentRequest})
        response.raise_for_status()
        return response.json()

    def settleinvoice(self,preimage):
        api_url = f"{self.base_url}/settleinvoice"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"preimage":preimage})
        response.raise_for_status()
        return response.json()

    def cancelinvoice(self,paymenthash):
        api_url = f"{self.base_url}/cancelinvoice"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"paymenthash":paymenthash})
        response.raise_for_status()
        return response.json()

    def getinvoice(self,paymenthash):
        api_url = f"{self.base_url}/getinvoice"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"paymenthash":paymenthash})
        response.raise_for_status()
        return response.json()

    def listinvoices(self):
        api_url = f"{self.base_url}/listinvoices"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()

    def listpayments(self):
        api_url = f"{self.base_url}/listpayments"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()

    def getpayment(self,paymenthash):
        api_url = f"{self.base_url}/getpayment"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"paymenthash":paymenthash})
        response.raise_for_status()
        return response.json()
    
    