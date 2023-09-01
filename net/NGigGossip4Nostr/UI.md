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
|  [ Recover Existing Profile ]  | 
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

## Trust Enforcers: Verification
You can skip this step if you only want to earn Bitcoin on message routing. If you want to order a ride or you are a driver this step is obligatory.

The app is using a network of Trust Enforcers that manage the trust and safty of the network. Trust Enforcers add an extra layer of security within the app's network. To be validated by one or more preferred Trust Enforcer, you need to go through a basic verification process involving mobile phone validation through SMS. Having mobile phone verification adds an extra layer of security.

```
+--------------------------------+
|   🛡️ Add Trust Enforcer        |
|                                |
|  Enter domain name of new      |
|  Trust Enforcer:               |
|                                |
|  [.........................]   |
|                                |
|  Enter your mobile number for  |
|  verification:                 |
|                                |
|  [+.....][..................]  |
|                                |
|         [Skip]     [ Add ➕ ]  |
|                                |
+--------------------------------+
```

In this interface, you need to provide two pieces of information:
1. **Domain name** of the new Trust Enforcer.
2. Your **mobile phone number**.

After entering these details, tap on `Add`. The Trust Enforcer will verify your mobile number as part of their validation process. Remember, providing accurate details ensures a smooth and secure experience.


You need to input the code that you received via SMS from the Trust Enforcer. After entering these details, tap on `Submit`. The Trust Enforcer will verify your code and if it is correct, you will be able to use it.

```
+--------------------------------------+
|        🛡️ Add Trust Enforcer         |
|                                      |
|   Enter the 6-digit SMS code sent    |
|   to your mobile number:             |
|                                      |
|         [.][.][.][.][.][.]           |
|                                      |
|                         [Submit]     |
|                                      |
+--------------------------------------+
```

Below is the list of validated Trust Enforcers where you can manage your list.

```
+---------------------------------------+
|       Trust Enforcer Management       |
|                                       |
|  Trust Enforcer 1 (Default)           |
|                [🗑️ Delete]            |
|                                       |
|  Trust Enforcer 2                     |
|  [Make Default] [🗑️ Delete]           |
|                                       |
|  Trust Enforcer 3                     |
|  [Make Default] [🗑️ Delete]           |
|                                       |
|----------------------------------------|
|         [ 🟢 Add New Trust Enforcer ]  |
+----------------------------------------+
```

Having multiple trust enforcers require you to set one as the default one.

## Setting up Lightning Wallet
Every node needs to have the lightning wallet set up. Fill out information about your lightning wallet to send payments/earn money. Without this setup, you won't be able to create new invoices.

```
+--------------------------------+
| ⚡️ Add Lightning Wallet         |
|                                |
|  Enter domain name your LND    |
|  wallet provider:              |
|                                |
|  [.........................]   |
|                                |
|                    [ Add ➕ ]  |
|                                |
+--------------------------------+
```

Your wallet provider will use your PublicKey to set up your account, which is why it's important to have the PrivateKey recovery set handy. This will be the only way to access your Bitcoin locked on the wallet provider account. Many trust Enforcers provide the LND wallet services as well.

## Deposit Information

There's a prompt encouraging the user to deposit Bitcoin into their wallet. There's a line of text: 'To deposit Bitcoin to your wallet, send it to the following address:'.

Below the prompt, there's a Bitcoin address where users can send their Bitcoins to. It's a long string of alphanumeric characters, representing the public key associated with your wallet.

Underneath your Bitcoin address, there's a placeholder for a QR Code. The QR Code is usually scanned by another device to automatically input your Bitcoin address. 

```
+--------------------------------------------+
| ⚡️ Your Lightning Wallet                    |
|                                            |
|💰 0.00000000 BTC                           |
|                                            |
| To deposit Bitcoin to your wallet,         |
| send it to the following address:          |
|                                            |
| bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh |
|                                            |
        ▄▄▄▄▄▄▄ ▄ ▄ ▄▄  ▄▄ ▄▄ ▄▄▄▄▄▄▄  
        █ ▄▄▄ █ ▄██▀▀▄▀▄▄▀▄▄▀ █ ▄▄▄ █  
        █ ███ █ █▀▀▄██▀▄▄█▄▄█ █ ███ █  
        █▄▄▄▄▄█ ▄ █▀▄▀▄ █▀▄▀▄ █▄▄▄▄▄█  
        ▄▄▄▄  ▄ ▄ █▄█ ▀▀██▄▀ ▄  ▄▄▄ ▄  
        ▀██▀ █▄ █▄█▄█ █ █ █▀█▀█▀█▄▄▄▀  
        ▀▀  ▄█▄ ███ ▄█▄▀▀▄▄ ▄  ▀ ▄▄█▀  
        ▄▀█▀▄▀▄▄█ ▄█▀ ▄▀▀█▀██▀█▄ ▄ ▀█  
        █ ██ ▀▄█▀▀▄▀▄▄▀▄  ▀  █▀ ▄█  ▀  
        ▄ █▄▀ ▄█ █▀   ▄█▄ ▀ ▀█ ▀▄█▀    
            ▄▀▄██▄ ▄  ▀  █▀ █ ▀███▄▄ █ 
        ▄▄▄▄▄▄▄ ▀▄▄  ▀▄  █ ██ ▄ █  █▀  
        █ ▄▄▄ █  ▀ █▀▀  █▄█ █▄▄▄█ ▄▄▄  
        █ ███ █ █ ▀▀▄▀█▀▄█▀ ▀█ █▀ ▄ █  
        █▄▄▄▄▄█ █▄▀▄▀▀ ▀▄ ▄███ ▀ ▀ █   

|                                            |
|   [Copy Address]     [Share QR code]       |
|                                            |
+--------------------------------------------+
```

## Withdraw Bitcoin

If you need to withdraw Bitcoin from your wallet, follow the steps outlined below. You also have the option of scanning a QR code instead of manually typing in the recipient's address.

```
+--------------------------------------------+
| ⚡️ Your Lightning Wallet                    |
|                                            |
|💰 0.32120000 BTC                           |
|                                            |
| Enter address to send Bitcoin:             |
|                                            |
| [.........................] [Scan QRcode]  |
|                                            |
| Enter amount (In BTC):                     |
|                                            |
| [............]                             |
|                                            |
|        [⛔ Cancel]       [✅ Confirm]       |
|                                            |
+--------------------------------------------+
```
There's also an option to scan a QR code for quick and error-free input of the desired recipient's address. Make sure your device camera is enabled for this feature.
```
+--------------------------------------------+
| 📸 Scan Recipient's QR Code                |
|                                            |
| Point your camera at the QR code.          |
|                                            |
|              +------+                      |
|              |      |                      |
|              |  🎯  |                      |
|              |      |                      |
|              +------+                      |
|                                            |
|        [⛔ Cancel]       [🆗 Continue]      |
|                                            |
+--------------------------------------------+
```

After you've scanned the QR code or entered the Bitcoin address manually, verify the details before confirming the transaction. 

Remember: transactions cannot be reversed once they're sent out!

