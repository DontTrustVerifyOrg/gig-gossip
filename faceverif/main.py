import base64
import os

import io
import numpy as np
from PIL import Image
import os
from cface import CFace
from flask import Flask, request, jsonify

cf = CFace('mtcnn')
print('mtcnn is loading')
cf.init_face_detector()
cf.init_face_recognition_models()

model_names = ["VGG-Face", "Facenet", "Facenet512", "ArcFace"]


def get_embeddings(img) :
    """
    Calculating all embedings values depending on models
    """
    results = cf.get_embeddings(img, model_names) #model_names)
    return results

app = Flask(__name__)

@app.route("/", methods=["GET", "POST"])
def index():
    if request.method == "POST":
        """
        file = request.files.get('file')
        if file is None or file.filename == "":
            return jsonify({"error": "no file"})
        """

        try:
            # print(request.json)
            if not request.json or 'image' not in request.json:
                os.abort(400)

            # get the base64 encoded string
            im_b64 = request.json['image']

            # convert it into bytes
            img_bytes = base64.b64decode(im_b64.encode('utf-8'))

            # convert bytes data to PIL Image object
            img = Image.open(io.BytesIO(img_bytes))

            img_arr = np.asarray(img)
            print('img shape', img_arr.shape)

            embedings = get_embeddings(img_arr)
            # data = {"prediction": int(prediction)}
            return jsonify(embedings)

            # PIL image object to numpy array
            #img_arr = np.asarray(img)

            #image_bytes = file.read()
            #pillow_img = Image.open(io.BytesIO(image_bytes)).convert('L')
            #embedings = get_embeddings(image_bytes)
            #data = {"prediction": int(prediction)}
            return jsonify(embedings)
        except Exception as e:
            return jsonify({"error": str(e)})

    return "OK"


if __name__ == "__main__":
    app.run(debug=True)