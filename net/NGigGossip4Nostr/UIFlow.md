# Here is the UI FLow diagram

```mermaid
flowchart TD
    APPL("`**START:**
    App Launch Screen`")
    FACEID("`**START:** 
    Face ID Scan`")
    LWYPK(Log In With Your PrivKey)
    CNP(Create New Profile)
    REP(Recover Existing Profile)
    AFID(Allow FaceID)
    subgraph " "
        UPKSET[Setting Up Trust Enforcer]
        ADDTE(Add Trust Enforcer)
    end
    APPL-->LWYPK
    APPL-->CNP
    APPL-->REP
    LWYPK-->AFID
    CNP-->AFID
    REP-->AFID
    AFID-->UPKSET
    FACEID-->UPKSET
    subgraph " "
        C3[Setting Up Lightning Wallet]
        ADDLW(Add Lightning Wallet)
        DEPBTC(Deposit Bitcoin)
    end
    REQRD(Request Ride)
    LFDR(Looking For A Driver)
    CYDR(Choose Your Driver)
    CODR(Confirm Your Ride)
    TYDR(Tracking your driver)
    DRAR(Driver arrived)
    RDCO(Ride Completed)
    UPKSET-->ADDTE
    ADDTE-->C3
    C3-->ADDLW
    UPKSET-->C3
    ADDLW-->DEPBTC
    DEPBTC-->REQRD
    C3-->REQRD
    REQRD-->LFDR
    LFDR-->CYDR
    CYDR-->CODR
    CODR-->TYDR
    TYDR-->DRAR
    DRAR-->RDCO
```