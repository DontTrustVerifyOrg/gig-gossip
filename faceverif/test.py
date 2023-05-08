import base64
import json

import requests

api = "https://getembeddings-tb7hp3iw2q-ts.a.run.app/" #"https://getembeddings-tb7hp3iw2q-ts.a.run.app/"#'http://localhost:5000/'
image_file = 'images/kemal1.jpg'

with open(image_file, "rb") as f:
    im_bytes = f.read()
im_b64 = base64.b64encode(im_bytes).decode("utf8")

headers = {'Content-type': 'application/json', 'Accept': 'text/plain'}
payload = json.dumps({"image": im_b64, "other_key": "value"})
response = requests.post(api, data=payload, headers=headers)
try:
    data = response.json()
    print(data)
except requests.exceptions.RequestException:
   print(response.text)

