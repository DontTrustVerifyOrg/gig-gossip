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
For Rider that is only willing to order the ride this step is optional. You can use the Lightning Network wallet of your choise to pay the invoices but you will not be able to earn money for gossiping because to do it your app needs to be able to issue HODL invoices automatically.

If you are the driver or want to earn money for gossiping this step is obligatory. fill out information about your lightning wallet to send payments/earn money. Without this setup, you won't be able to create new invoices.

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
| 📸 Scan Bitcoin Address from QR Code       |
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

Remember: transactions cannot be reversed once they're sent out so be carefull where you are sending your Bitcoin!

# Request Ride

In order to request a ride, start by entering your pickup and destination addresses. You have the option to pinpoint your location on the map as well.

```
+--------------------------------------------+
| 🚖  Request a Ride                          |
|                                            |
|   Choose Pickup Location:                  |
|                                            |
| [.........................]                |
|    [📍 Pick on Map]                        |
|                                            |
|   Choose Destination Location:             |
|                                            |
| [.........................]                |
|    [📍 Pick on Map]                        |
|                                            |
|          [⛔ Cancel]     [✅ Request]      |
|                                            |
+--------------------------------------------+
```

If you want to select your location through the map instead of typing in your address, you can tap on `[📍 Pick on Map]`. This will transition to a different interface where you can drag and drop a pin to specify your exact location.

```
+--------------------------------------------+
| 🗺  Pick Location on Map                   |
|                                            |
                _,__        .:
         Darwin <*  /        | \
            .-./     |.     :  :,
           /           '-._/     \_
          /                '       \
        .'                         *: Brisbane
     .-'                             ;
     |                               |
     \                              /
      |                           🔵  Sydney
Perth  \*        __.--._      [DROP PIN]
        \     _.'       \:.       |
        >__,-'             \_/*_.-'
                              Melbourne
                             :--,
                              '/
|                                            |
|          [⛔ Cancel]     [✅ Confirm]      | 
|                                            |
+--------------------------------------------+
```
After you set your locations, tap on `Request`. Your request will be sent to the drivers near your area, and you'll get notified once a driver accepts your request.

## Looking For A Driver

Once your ride request is sent, the app will start searching for a driver near your location. 

You'll see the following screen while the app is finding a driver:

```
+--------------------------------------------+
| 🚖  Requesting a Ride                      |
|                                            |
|    Please wait...                          |
|                                            |
|   [🔄..........................]           |
|       Searching for driver                 |
|                                            |
|      [Cancel Ride Request ⛔]               |
|                                            |
+--------------------------------------------+
```

This process may take several minutes depending on the availability of drivers in your area.

If you wish to cancel the ride request for any reason, simply tap on `Cancel Ride Request`. In that case, the app will stop searching for a driver and you will return to the main screen.
 

## Choose Your Driver

Once multiple drivers accept your request, you can choose the best suited driver for your journey. Here's how the screen will look like:

```
+--------------------------------------------+
| 🚖  Select Your Ride                       |
|                                            |
| Sorted by: Lowest Price                    |
| [Change Sort ▾]                            |
|                                            |
|         NAME       |   TRUST ENFORCER      |
|       PRICE        |     ETA               |
|--------------------------------------------|
| Driver A           | Trust Enforcer 1      |
| 0.00003 BTC        | 5 min                 |
|      [Select 🚙]                           |
|--------------------------------------------|
| Driver B           | Trust Enforcer 2      |
| 0.00004 BTC        | 3 min                 |
|      [Select 🚙]                           |
|--------------------------------------------|
| Driver C           | Trust Enforcer 3      |
| 0.00005 BTC        | 6 min                 |
|      [Select 🚙]                           |
|                                            |
+--------------------------------------------+
```

In this screen, you'll see a list of drivers sorted by price from lowest to highest. Besides each driver’s name, you'll see their associated Trust Enforcer and the price they are asking for the ride. You'll also see their estimated time of arrival (ETA) at your pickup point.

To choose a driver, simply tap on `Select` next to the driver’s details. This will confirm your ride details and the selected driver will be notified.

You have the option to sort the list in different ways by tapping on `Change Sort`, such as by ETA or Trust Enforcer.

This selection process empowers you with choice allowing you to opt for the most affordable, fastest or most trusted ride based on your personal preferences.

## Confirm Your Ride

Now that you have selected "Driver B", the screen will look like this:

```
+--------------------------------------------+
| 🚖  Confirm Your Ride                      |
|                                            |
|        Driver: Driver B                    |
|   Trust Enforcer: Trust Enforcer 2         |
|          Price: 0.00004 BTC                |
|            ETA: 3 min                      |
|                                            |
| To confirm this ride, your payment will be |
| LOCKED with Trust Enforcer 2. Please make  |
| sure that you want to proceed before       |
| accepting.                                 |
|                                            |
|    ❗WARNING❗                              |
| Accepting this ride will lock your payment |
| with Trust Enforcer 2 until the ride       |
| is completed. This action cannot be undone.|
| Afer confirming, the cancellation fee      |
| will be applied.                           |  
|                                            |
|               [Back 🔙]                    |
|               [Confirm ✔️]                  |
|                                            |
+--------------------------------------------+
```

In this screen,

* You are about to confirm a ride with "Driver B". 
* The price of the ride and its ETA is displayed.
* There's a warning message that your payment will be locked by "Trust Enforcer 2" upon confirmation.
* Two options are provided at the bottom of the screen
  * `Back` - to go back to the list of drivers.
  * `Confirm` - to finalize the ride and lock your payment.

> Please note: it is important to read the warning message before confirming. 

## Tracking your driver

Here's a map showing how your driver is approaching:

```
+--------------------------------------------+
| 🗺  Track Your Driver                      |
|                                            |
                _,__        .:
         Darwin <*  /        | \
            .-./     |.     :  :,
           /  🚖         '-._/     \_
          /    |            '       \
        .'     |                    *: Brisbane
     .-'       |                     ;
     |         +--------+            |
     \                  |            /
      |                 +--------- 🔵  Sydney
Perth  \*        __.--._      [YOU ARE HERE]
        \     _.'       \:.       |
        >__,-'             \_/*_.-'
                              Melbourne
                             :--,
                              '/ 
|                                            |
|       [⛔ Cancel Ride]                     | 
|                                            |
+--------------------------------------------+
```
In addition to showing your location, this screen also displays the moving position of your Driver as an icon (🚖) on the map. The driver is currently near Darwin and is on their way to your pinned location in Sydney.

You have two options at this point:
* `Cancel Ride` - if you no longer need the ride you can cancel but the cancellation fee will be applied.
* `Continue` - to keep tracking the driver.

# Driver arrived
```
+--------------------------------------------+
| 🗺  Your Driver Has Arrived                |
|                                            |
                _,__        .:
         Darwin <*  /        | \
            .-./     |.     :  :,
           /           '-._/     \_
          /                '       \
        .'                         *: Brisbane
     .-'                             ;
     |                               |
     \                              /
      |                           🚖  Sydney
Perth  \*        __.--._      [YOU ARE HERE]
        \     _.'       \:.       |
        >__,-'             \_/*_.-'
                              Melbourne
                             :--,
                              '/ 
|                                            |
|    👤 Driver: Driver B                     |
|    🔒 Trust Enforcer: Trust Enforcer 2     |
|                                            |
|       [⚖️ Start Dispute]                   | 
|                                            |
+--------------------------------------------+
```

The icon (🚖) on the map now reflects that your driver is already at your location in Sydney, waiting for you.

* `Start Dispute` - If there's a problem with your ride – like the driver didn't arrive, or there's an quality issue you can start a dispute here. The dispute will be resolved by the associated Trust Enforcer, which for this ride is Trust Enforcer 2.

## Ride Completed

Once the ride is completed successfully, you will see the following screen:

```
+--------------------------------------------+
| 🚖  Ride Completed                         |
|                                            |
|   Your ride with Driver B has been         |
|   completed.                               |
|                                            |
|   Please take a moment to rate your ride.  |
|                                            |
|                                            |
|    [⭐] [⭐] [⭐] [⭐] [  ]                |
|                                            |
|    [Submit Rating]                         |
|                                            |
|    [⚖️ Start Dispute]                       | 
|                                            |
+--------------------------------------------+
```

On this screen, you'll have the option to:

* Rate your driver by selecting between 1 (poor) and 5 (excellent) stars. After making your selection, tap `Submit Rating`. Providing feedback after each ride helps Trust Enforcers to ensure the quality of service. Honest ratings help other riders when choosing their drivers in future trips!

* Alternatively, if there were any issues during the ride, you can start a dispute by tapping on `Start Dispute`. This will notify your Trust Enforcer (in this case, Trust Enforcer 2), who will work towards resolving the dispute as quickly as possible. Your money will be unlocked after the timeout if this was the driver fault.

# Driver perspecive


## Driver parameters setup
Here is the screen where drivers can set their prices based on Door Closing, Per Kilometer, and ETA (Estimated Time of Arrival).

```markdown
+--------------------------------------------+
| 🚖  Set Your Pricing                       |
|                                            |
|   Door Closing Fee:                        |
| [.........................] BTC            |
|                                            |
|   Price Per Kilometer:                     |
| [.........................] BTC            |
|                                            |
|   ETA Premium (for quick arrivals):        |
| [.........................] BTC            |
|                                            |
|            [⛔ Cancel]                     |
|         [✔️ Save Pricing Info]             |
+--------------------------------------------+
```

In this screen:

- Drivers have an option to set a `Door Closing Fee`.
- They can specify the price per kilometer under `Price Per Kilometer`.
- Drivers can also add an additional fee for quick arrivals in the `ETA Premium`.

Word of caution: always align your pricing with market rates to increase the chances of getting selected by riders.

## New Ride Request Notification

Drivers will receive requests for new rides. The notification will include basic information about the requested ride.

```
+--------------------------------------------+
| 🚖  New Ride Request                        |
|                                            |
|   Rider: Rider A     | Phone Number Verified by Trust Enforcer 1                      |
|                                            |
|   Pickup Location:                         |
|   Sydney CBD                               |
|                                            |
|   Destination Location:                    |
|   Sydney Airport                           |
|                                            |
|   Estimated Distance: 15 KM                |
|                                            |
|   Your Price (based on your settings):     |
|        0.00006 BTC  [Modify]               |
|                                            |
|                  [Ignore ⛔]               |
|               [Accept Ride ✔️]              |
+--------------------------------------------+
```

## Price bet modification

```markdown
+--------------------------------------------+
| 🚖  Set Your Price                         |
|                                            |
|   Rider: Rider A  | Phone Number Verified by Trust Enforcer 1                      |
|                                            |
|   Pickup Location:                         |
|   Sydney CBD                               |
|                                            |
|   Destination Location:                    |
|   Sydney Airport                           |
|                                            |
|   Estimated Distance: 15 KM                |
|                                            |
|   Current Price (based on your settings):  |
|        0.00006 BTC                         |
|                                            |
|      New Price:                            |
| [.........................]                |
|                                            |
|            [⛔ Cancel]                     |
|     [✔️ Confirm New Price]                 |
+--------------------------------------------+
```
- Drivers are displayed their current price and have an option to set a new price.
- To do this, they need to enter the new price in the provided input field and then select `Confirm New Price`.
- If the driver changes their mind and does not want to change the price, they simply press `Cancel` to go back to the previous screen.

## Waiting for riders approval (locking their money)

```markdown
+--------------------------------------------------+
| 🚖  Awaiting Rider Approval                       |
|                                                  |
|                         ⌛                        |
|            Your ride request is pending...       |
| Please wait while we get the rider's approval.   |
|                                                  |
| You will be notified once the rider has accepted |
| or declined your request. Please stay on this    |
| screen until we update you.                      |
|                                                  |
|                 [Cancel Request]                 |
+--------------------------------------------------+
```

In this screen:

- The hourglass emoji represents the waiting process.
- There's a message informing the driver that their ride request is pending and they need to wait for the rider's approval.
- It mentions that the driver will be notified once the rider accepts or declines the request, and advises them to stay on this screen until this happens.
- If the driver wants to cancel the request for any reason, they can simply click on the `Cancel Request` button.

## Ride approved
Here's a simple representation of the "Navigation Screen" after the rider has approved the ride:

```markdown
+--------------------------------------------------+
| 🚖  Ride Approved - Start the Journey!            |
|                                                  |
| Rider: John Doe      Destination: Central Park   |
|                                                  |
|           🧭 Navigation Guidance 🧭              |
|                                                  |
| ↗️  Drive straight for 2.0 km                    |
| ↘️  Turn right onto Main St                      |
| ↙️  Merge left into Highway Rd                   |
| ↖️  Take exit 43 for Central Park                |
|                                                  |
| [End Navigation]   [Contact Rider]  [Start Dispute]             |
+--------------------------------------------------+
```
