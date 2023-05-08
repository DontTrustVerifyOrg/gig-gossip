import cv2
import matplotlib.pyplot as plt
import os
from cface import CFace
from deepface import DeepFace

cf = CFace('mtcnn')

def verify():
    """
    Compares two images by 4 models. If the correct identification number in 4 models is equal
    to and greater than the minVerifyNumber value,
    it is decided that the faces in the two images belong to the same person.
    """
    img_path1 = "images/kemal1.jpg"
    img_path2 = "images/kemal2.jpg"
    resp_objects = cf.get_distances(img_path1, img_path2, normalization='base')

    accepted_status, accepted_count = cf.verify(resp_objects, minVerifyNumber=2)

    print ("accepted", accepted_status , ", similarity count :", accepted_count)


def get_face_from_imagefile():
    """
    Returns : finds the largest face from the image
    detected_face as rgb
    :return:
    """
    img_path = "images/kemal1.jpg"
    detected_face, img_region = cf.get_face_from_imagefile(img_path = img_path)
    if detected_face is not None :
        plt.imshow(detected_face)

def get_embeddings() :
    """
    Calculating all embedings values depending on models
    """
    model_names = ["VGG-Face", "Facenet", "Facenet512", "ArcFace"]
    img_path = "images/kemal1.jpg"
    results = cf.get_embeddings_specified_models(img_path, model_names)
    for res in results:
        #embeddings, model = res
        print(res)

def extract_all_face_and_save() :
    """
    It extracts all faces from the images and alligns them, then saves them.
    Of course, since the faces he took out will belong to other people,
    you need to clean the different faces after the registration process is finished.
    :return:
    """
    images_path = "images/kemalkilicdaroglu"
    saved_directory_path = "dataset/kilicdaroglu"
    cf.extract_all_face_and_save(images_path = images_path, saved_directory_path = saved_directory_path)

def get_distances() :
    """
    compares two faces in two pictures.
    measures the distance according to the model , cosine, euclidean, euclidean l2 metrics
    """
    img1_path = 'images/imamoglu1.jpg'
    img2_path = 'images/imamoglu2.jpg'

    results = cf.get_distances(img1_path, img2_path, normalization='base')
    for res in results :
        #verified, distance, max_threshold_to_verify, model = res
        print (res)


def cross_false_validation () :
    """
    The faces of two different people are compared.
    Our expectation is that the values will be low.

    The faces of two people are compared in a diagonal fashion.
    if there are 10 faces belonging to person A in the 1st folder and 15 faces belonging to the B person in the 2nd folder.
    a total of 10 * 15 = 150 comparisons are made. 4 models were used in the comparison.
    For example, let's say the following result is obtained for FaceNet.
        {'model_name': 'Facenet', 'metric': 'cosine', 'accuracy': 0.0}

    The accuracy value is 0.0, which means that none of these 150 comparisons are correctly matched.
    The lower the accuracy, the more successful the discrimination is.

    Example :
        {'model_name': 'VGG-Face', 'accuracy': 0.16905}
        {'model_name': 'Facenet', 'accuracy': 0.0}
        {'model_name': 'Facenet512', 'accuracy': 0.0}
        {'model_name': 'ArcFace', 'accuracy': 0.00265}

    """
    face_img_path1 = 'dataset/azizsancar'
    face_img_path2 = 'dataset/barisatay'

    results = cf.cross_false_validation(face_img_path1 = face_img_path1, face_img_path2 = face_img_path2)
    for res in results :
        # model_name, metric, accuracy = res
        print (res)

def cross_validation_all_data() :
    path = 'dataset'
    my_list = os.listdir(path)
    all_results = []

    for dirname in my_list:
        print("[" + dirname + "]")
        results = cf.cross_validation(os.path.join(path, dirname))
        for res in results:
            print(res)
        all_results.append(results)

    print("all model \n")
    for model_name in cf.model_names:
        for res in all_results:
            for dic in res:
                for key, value in dic.items():
                    if key == 'model_name' and value == model_name:
                        print(dic)
                        break

def cross_validation() :
    """
    all faces belonging to a person are compared within themselves according to the models.
    For faces belonging to the same person, the total accuracy value is calculated for each model.
    Accuracy here is expected to be very high
    """
    path = 'dataset/azizsancar'
    results = cf.cross_validation(path)
    for res in results:
        print(res)

def verify_main() :

    cf.init_face_detector()
    cf.init_face_recognition_models()

    tm = cv2.TickMeter()
    tm.start()

    #get_face_from_imagefile()

    #get_embeddings()

    #extract_all_face_and_save()

    #get_distances()

    #cross_validation_all_data()

    #cross_false_validation()

    #cross_validation()

    verify()

    tm.stop()
    print('inference ms {}', tm.getTimeMilli())

if __name__ == '__main__':
    verify_main()