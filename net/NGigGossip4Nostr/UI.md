# Profile Setup Guide 

Setting up your profile is a breeze. The unique identifier is your `pubkey` and you sign messages with your `privkey` in the Nostr protocol. Remember, the `pubkey` gets derived from the `privkey`, hence generating a `privkey` becomes the initial step for new users. Make sure to store your `privkey` safely.

## Getting Started: App Launch Screen

Once you install and launch the app, choose the login option:

```
+--------------------------------+
|   🎉 Setup Your Profile        |
|                                |
|  [ Login With Your PrivKey 🔑] |
|  [ Create New Profile 📝 ]     |
|  [ Recover Existing Profile]   | 
|                                |
+--------------------------------+
```
## Quick Access: Log In With Your PrivKey

You can swiftly access the platform if you already have your privkey.

```
+--------------------------------+
|   🔒Login With Your PrivKey    |
|                                |
|  Enter your PrivKey:           |
|                                |
|  [.........................]   |
|                                |
|                    [ Next ➡️ ]  |
|                                |
+--------------------------------+
```

## Start Fresh: Create New Profile

Alternatively, you can also create a new profile which primarily involves generating your recovery mnemonic. Ensure you write these 12 words down. These words are generated using BIP39 ([https://bips.xyz/39](https://bips.xyz/39)).

```
+--------------------------------+
|   💼 Create New Profile        |
|                                |
|  Your recovery mnemonic:       |
|                                |
| +----------------------------+ |
| | image  debate else  control| |
| | amused inform salon slow   | |
| | chief  divide route apology| |
| +----------------------------+ |
|                                |
|  Write them down now notepad!  |
|                    [ Next ➡️ ]  |
|                                |
+--------------------------------+
```

## Backup Plan: Recover Existing Profile

If you already have your recovery mnemonic, select the `Recover Existing Profile`. This will lead to the recreation of your privkey from the seed encoded in the mnemonic.

```
+--------------------------------+
|   ⏪ Recover Existing Profile  |
|                                |
|  Enter your recovery mnemonic: |
|                                |
|  [....] [....] [....] [....]   |
|  [....] [....] [....] [....]   |
|  [....] [....] [....] [....]   |
|                                |
|                    [ Next ➡️ ]  |
|                                |
+--------------------------------+
```

## Added Security: Allow FaceID

To further enhance security, the app employs the iPhone's FaceID feature to secure the account and its details.

```
+--------------------------------+
|   👤 Allow FaceID              |
|                                |
|  Would you like to enable      |
|  FaceID for this app?          |
|                                |
|                    [ Yes ✔️ ]   |
|                    [ No ❌ ]   |
|                                |
+--------------------------------+
```

A quick scan secures your login.

```
+--------------------------------+
|                                |
|         🔒 Login               |
|                                |
|   Scanning your face...        |
|          +-     -+             |
|          | o J o |             |
|          | \___/ |             |
|          +-     -+             |
|                                |
| If you're having trouble,      |
| try repositioning your face or |
| scanning again.                |
|                                |
+--------------------------------+
```