# %%
import crypto

private_key,public_key = crypto.create_keys()

# %%

obj = ["ala",["ma","kota"]]*10000
cryp=crypto.encrypt_object(obj,public_key)
# %%
crypto.decrypt_object(cryp,private_key)
# %%
len(cryp)
# %%

private_key2,public_key2 = crypto.create_keys()
crypto.decrypt_object(cryp,private_key2)

# %%
