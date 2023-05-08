
import os

os.environ['TF_CPP_MIN_LOG_LEVEL'] = '3'
import cv2
import base64
from PIL import Image
import requests
from deepface.detectors import FaceDetector
import numpy as np
from tqdm import tqdm
import glob

from deepface.basemodels import VGGFace, Facenet, Facenet512, ArcFace
from deepface.extendedmodels import Age, Gender, Race, Emotion
from deepface.commons import functions, realtime

import tensorflow as tf

tf_version = int(tf.__version__.split(".")[0])
if tf_version == 2:
    import logging

    tf.get_logger().setLevel(logging.ERROR)

import tensorflow as tf

tf_version = tf.__version__
tf_major_version = int(tf_version.split(".")[0])
tf_minor_version = int(tf_version.split(".")[1])

if tf_major_version == 1:
    from keras.preprocessing import image
elif tf_major_version == 2:
    from tensorflow.keras.preprocessing import image


class CFace:
    detector_model_name = 'mtcnn'
    model_names = ["VGG-Face", "Facenet", "Facenet512", "ArcFace"]
    metrics = ["cosine"]

    # init method or constructor
    def __init__(self, detector_backend):
        # self.detector_xx = name
        self.detector_backend = detector_backend

    def init_face_recognition_models(self):
        self.models = self.loadModel()

    def init_face_detector(self):
        self.face_detector = FaceDetector.build_model(self.detector_backend)

    def build_model(self, model_name):

        global model_obj  # singleton design pattern

        models = {
            'VGG-Face': VGGFace.loadModel,
            'Facenet': Facenet.loadModel,
            'Facenet512': Facenet512.loadModel,
            'ArcFace': ArcFace.loadModel,
            'Emotion': Emotion.loadModel,
            'Age': Age.loadModel,
            'Gender': Gender.loadModel,
            'Race': Race.loadModel
        }

        if not "model_obj" in globals():
            model_obj = {}

        if not model_name in model_obj.keys():
            model = models.get(model_name)
            if model:
                model = model()
                model_obj[model_name] = model
                # print(model_name," built")
            else:
                raise ValueError('Invalid model_name passed - {}'.format(model_name))

        return model_obj[model_name]

    def loadModel(self):

        model = {}

        model_pbar = tqdm(range(0, len(self.model_names)), desc='Face recognition models')

        for index in model_pbar:
            model_name = self.model_names[index]

            model_pbar.set_description("Loading %s" % (model_name))
            model[model_name] = self.build_model(model_name)

        return model

    def get_true_verified_number(self, resp_objects):
        accepted_count = 0
        for dic in resp_objects:
            for key, value in dic.items():
                if key == 'verified' and value == True:
                    accepted_count += 1

        return accepted_count

    def verify(self, resp_objects, minVerifyNumber=2):

        accepted_count = self.get_true_verified_number(resp_objects)
        if (accepted_count >= minVerifyNumber):
            return True, accepted_count
        else:
            return False, accepted_count

    def get_face_from_imagefile(self, img_path):
        img1 = self.load_image(img_path)

        faceimg, region = self.get_face_img(img=img1)

        return faceimg[:,:,::-1], region


    def findCosineDistance(self, e1, e2):
        a = np.matmul(np.transpose(e1), e2)
        b = np.sum(np.multiply(e1, e1))
        c = np.sum(np.multiply(e2, e2))
        return 1 - (a / (np.sqrt(b) * np.sqrt(c)))

    def findThreshold(self, model_name):

        thresholds = {
            'VGG-Face': 0.30,
            'Facenet': 0.40,
            'Facenet512': 0.30,
            'ArcFace': 0.68
        }

        threshold = thresholds.get(model_name)

        return threshold

    def get_distances(self, img1_path='', img2_path='', normalization='base'):

        resp_objects = []

        img1 = self.load_image(img1_path)
        img2 = self.load_image(img2_path)

        faceimg1, region1 = self.get_face_img(img=img1)
        faceimg2, region2 = self.get_face_img(img=img2)

        for model_name in self.model_names:

            custom_model = self.models[model_name]

            img1_representation = self.represent(faceimg1, model=custom_model, normalization=normalization)

            img2_representation = self.represent(faceimg2, model=custom_model, normalization=normalization)

            # ----------------------
            # find distances between embeddings

            distance = self.findCosineDistance(img1_representation, img2_representation)

            distance = np.float64(distance)
            # ----------------------
            # decision

            threshold = self.findThreshold(model_name)

            if distance <= threshold:
                identified = True
            else:
                identified = False

            resp_obj = {
                "verified": identified
                , "distance": distance
                , "max_threshold_to_verify": threshold
                , "model": model_name
            }
            resp_objects.append(resp_obj)

        return resp_objects

    def loadBase64Img(self, uri):
        encoded_data = uri.split(',')[1]
        nparr = np.fromstring(base64.b64decode(encoded_data), np.uint8)
        img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        return img

    def load_image(self, img):
        exact_image = False;
        base64_img = False;
        url_img = False

        if type(img).__module__ == np.__name__:
            exact_image = True

        elif len(img) > 11 and img[0:11] == "data:image/":
            base64_img = True

        elif len(img) > 11 and img.startswith("http"):
            url_img = True

        # ---------------------------

        if base64_img == True:
            img = self.loadBase64Img(img)

        elif url_img:
            img = np.array(Image.open(requests.get(img, stream=True).raw))

        elif exact_image != True:  # image path passed as input
            if os.path.isfile(img) != True:
                raise ValueError("Confirm that ", img, " exists")

            img = cv2.imread(img)

        return img

    def get_face_img(self, img):
        # img might be path, base64 or numpy array. Convert it to numpy whatever it is.

        img_region = [0, 0, img.shape[0], img.shape[1]]

        try:
            detected_faces = FaceDetector.detect_faces(self.face_detector, self.detector_backend, img, align=True)

            index = 0
            maxarea = 0
            for i in range(len(detected_faces)):
                faceimg, region = detected_faces[i]
                area = region[2] * region[3]
                if (area > maxarea):
                    maxarea = area
                    index = i

            detected_face, img_region = detected_faces[index]

            # detected_face, img_region = FaceDetector.detect_faces(self.face_detector, self.detector_backend, img, align=True)
        except:  # if detected face shape is (0, 0) and alignment cannot be performed, this block will be run
            detected_face = None

        # cv2.imwrite('faces/face1.jpg', detected_face)

        if (isinstance(detected_face, np.ndarray)):
            return detected_face, img_region
        else:
            if detected_face == None:
                return None, None

    def extract_all_face_and_save(self, images_path, saved_directory_path):

        if not os.path.isdir(saved_directory_path):
            os.mkdir(saved_directory_path)

        image_list = []
        for filename in glob.glob(images_path + '/*.jpg'):  # assuming gif
            image_list.append(filename)

        idx = 0
        for filename in image_list:
            img = self.load_image(filename)

            if img is None:
                continue

            # base_img = img.copy()
            img_region = [0, 0, img.shape[0], img.shape[1]]

            try:
                detected_face, img_region = FaceDetector.detect_face(self.face_detector, self.detector_backend, img,
                                                                     align=True)

                cv2.imwrite(saved_directory_path + '/' + str(idx) + '.jpg', detected_face)
                idx += 1

            except:  # if detected face shape is (0, 0) and alignment cannot be performed, this block will be run
                detected_face = None

    def align_face(self, img, target_size=(224, 224)):

        if img.shape[0] > 0 and img.shape[1] > 0:
            factor_0 = target_size[0] / img.shape[0]
            factor_1 = target_size[1] / img.shape[1]
            factor = min(factor_0, factor_1)

            dsize = (int(img.shape[1] * factor), int(img.shape[0] * factor))
            img = cv2.resize(img, dsize)

            # Then pad the other side to the target size by adding black pixels
            diff_0 = target_size[0] - img.shape[0]
            diff_1 = target_size[1] - img.shape[1]
            # Put the base image in the middle of the padded image
            img = np.pad(img, ((diff_0 // 2, diff_0 - diff_0 // 2), (diff_1 // 2, diff_1 - diff_1 // 2), (0, 0)),
                         'constant')

        # ------------------------------------------

        # double check: if target image is not still the same size with target.
        if img.shape[0:2] != target_size:
            img = cv2.resize(img, target_size)

        # ---------------------------------------------------

        # normalizing the image pixels

        img_pixels = image.img_to_array(img)  # what this line doing? must?
        img_pixels = np.expand_dims(img_pixels, axis=0)
        img_pixels /= 255  # normalize input in [0, 1]

        # ---------------------------------------------------
        return img_pixels

    def get_embeddings(self, img, models):
            list_embeddings = []
            faceimg, region = self.get_face_img(img)
            if faceimg is None:
                return list_embeddings
            for mname in models:
                if mname in self.model_names:
                    custom_model = self.models[mname]
                    embeddings = self.represent(faceimg, model=custom_model, normalization='base')
                    resp_obj = {
                        "model": mname,
                        "embeddings": embeddings
                    }
                    list_embeddings.append(resp_obj)
            return list_embeddings


    def get_embeddings_specified_models(self, img_path, models):
        list_embeddings = []
        if os.path.exists(img_path) == True:
            img = self.load_image(img_path)
            return self.get_embeddings(img, models)


    def cross_false_validation(self, face_img_path1, face_img_path2):
        list_embeddings1 = []
        list_embeddings2 = []
        for model_name in self.model_names:
            custom_model = self.models[model_name]
            list1 = []
            for filename in glob.glob(face_img_path1 + '/*.jpg'):
                faceimg = self.load_image(filename)
                embeddings = self.represent(faceimg, model=custom_model, normalization='base')
                list1.append(embeddings)

            list_embeddings1.append(list1)

            list2 = []
            for filename in glob.glob(face_img_path2 + '/*.jpg'):
                faceimg = self.load_image(filename)
                embeddings = self.represent(faceimg, model=custom_model, normalization='base')
                list2.append(embeddings)

            list_embeddings2.append(list2)

        all_results = []

        idx = 0
        for model_name in self.model_names:
            arr_embeddings1 = list_embeddings1[idx]
            arr_embeddings2 = list_embeddings2[idx]
            resp_objects = []
            for i in range(len(arr_embeddings1)):
                for j in range(len(arr_embeddings2)):

                    distance = self.findCosineDistance(arr_embeddings1[i], arr_embeddings2[j])

                    distance = np.float64(distance)
                    # ----------------------
                    # decision

                    threshold = self.findThreshold(model_name)

                    if distance <= threshold:
                        identified = True
                    else:
                        identified = False

                    resp_obj = {
                        "verified": identified
                        , "distance": distance
                        , "max_threshold_to_verify": threshold
                        , "model": model_name
                    }
                    resp_objects.append(resp_obj)

            accepted_count = self.get_true_verified_number(resp_objects)
            accuracy = float(accepted_count) / len(resp_objects)
            result = {
                'model_name': model_name,
                'accuracy': accuracy
            }
            all_results.append(result)
        idx += 1
        return all_results  # accuracy, len(resp_objects)


    def cross_validation(self, face_img_path):
        list_embeddings = []
        for model_name in self.model_names:
            custom_model = self.models[model_name]
            list = []
            for filename in glob.glob(face_img_path + '/*.jpg'):
                faceimg = self.load_image(filename)
                embeddings = self.represent(faceimg, model=custom_model, normalization='base')
                list.append(embeddings)

            list_embeddings.append(list)

        all_results = []

        idx = 0
        for model_name in self.model_names:
            all_embeddings = list_embeddings[idx]
            resp_objects = []
            for i in range(len(all_embeddings) - 1):
                for j in range(i + 1, len(all_embeddings)):

                    distance = self.findCosineDistance(all_embeddings[i], all_embeddings[j])

                    distance = np.float64(distance)
                    # ----------------------
                    # decision

                    threshold = self.findThreshold(model_name)

                    if distance <= threshold:
                        identified = True
                    else:
                        identified = False

                    resp_obj = {
                        "verified": identified
                        , "distance": distance
                        , "max_threshold_to_verify": threshold
                        , "model": model_name

                    }
                    resp_objects.append(resp_obj)

                accepted_count = self.get_true_verified_number(resp_objects)
                accuracy = float(accepted_count) / len(resp_objects)
                result = {
                    'model_name': model_name,
                    'accuracy': accuracy
                }
                all_results.append(result)
            idx += 1
        return all_results  # accuracy, len(resp_objects)


    def represent(self, faceimg, model, normalization='base'):
        # decide input shape
        input_shape_x, input_shape_y = functions.find_input_shape(model)

        img = self.align_face(faceimg, target_size=(input_shape_y, input_shape_x))
        # ---------------------------------
        # custom normalization

        img = functions.normalize_input(img=img, normalization=normalization)

        # represent
        embedding = model.predict(img)[0].tolist()

        return embedding


# ---------------------------
# main

functions.initialize_folder()
