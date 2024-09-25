
using System;
using System.Diagnostics;
using GigGossip;
using GigLNDWalletAPIClient;
using GoogleApi.Entities.Maps.StreetView.Request.Enums;
using NBitcoin;
using NetworkClientToolkit;
using NGigGossip4Nostr;

namespace RideShareCLIApp;

public class GigGossipNodeEvents : IGigGossipNodeEvents
{
    private readonly GigGossipNodeEventSource _gigGossipNodeEventSource;

    public GigGossipNodeEvents(GigGossipNodeEventSource gigGossipNodeEventSource)
    {
        _gigGossipNodeEventSource = gigGossipNodeEventSource;
    }

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        _gigGossipNodeEventSource.FireOnAcceptBroadcast(new AcceptBroadcastEventArgs()
        {
            GigGossipNode = me,
            PeerPublicKey = peerPublicKey,
            BroadcastFrame = broadcastFrame
        });
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnNetworkInvoiceAccepted(new NetworkInvoiceAcceptedEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnNetworkInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnNetworkInvoiceSettled(new NetworkInvoiceSettledEventArgs()
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnJobInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnJobInvoiceSettled(new JobInvoiceSettledEventArgs()
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnNewResponse(GigGossipNode me, JobReply replyPayloadCert, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
    {
        _gigGossipNodeEventSource.FireOnNewResponse(new NewResponseEventArgs()
        {
            GigGossipNode = me,
            ReplyPayloadCert = replyPayloadCert,
            ReplyInvoice = replyInvoice,
            DecodedReplyInvoice = decodedReplyInvoice,
            NetworkPaymentRequest = networkInvoice,
            DecodedNetworkInvoice = decodedNetworkInvoice,
        });
    }

    public void OnResponseReady(GigGossipNode me, JobReply replyPayload, string key)
    {
        var reply = replyPayload.Header.EncryptedReply.Decrypt<Reply>(key.AsBytes());

        _gigGossipNodeEventSource.FireOnResponseReady(new ResponseReadyEventArgs()
        {
            GigGossipNode = me,
            ReplierCertificateId = replyPayload.Header.JobReplyId.AsGuid(),
            RequestPayloadId = replyPayload.Header.JobRequest.Header.JobRequestId.AsGuid(),
            Reply = reply
        });
    }


    public void OnResponseCancelled(GigGossipNode me, JobReply replyPayload)
    {
        _gigGossipNodeEventSource.FireOnResponseCancelled(new ResponseCancelledEventArgs()
        {
            GigGossipNode = me,
            ReplierCertificateId = replyPayload.Header.JobReplyId.AsGuid(),
            RequestPayloadId = replyPayload.Header.JobRequest.Header.JobRequestId.AsGuid(),
        });
    }

    public void OnPaymentStatusChange(GigGossipNode me, PaymentStatus status, PaymentData paydata)
    {
        _gigGossipNodeEventSource.FireOnPaymentStatusChange(new PaymentStatusChangeEventArgs()
        {
            GigGossipNode = me,
            PaymentData = paydata,
            Status = status
        });
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
        _gigGossipNodeEventSource.FireOnCancelBroadcast(new CancelBroadcastEventArgs
        {
            GigGossipNode = me,
            CancelBroadcastFrame = broadcastFrame,
            PeerPublicKey = peerPublicKey
        });
    }

    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnNetworkInvoiceCancelled(new NetworkInvoiceCancelledEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnJobInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnJobInvoiceAccepted(new JobInvoiceAcceptedEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnJobInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnJobInvoiceCancelled(new JobInvoiceCancelledEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
        _gigGossipNodeEventSource.FireOnNewContact(new NewContactEventArgs
        {
            GigGossipNode = me,
            PublicKey = pubkey
        });
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
    }

    public void OnEoseArrived(GigGossipNode me)
    {
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
        _gigGossipNodeEventSource.FireOnServerConnectionState(new ServerConnectionSourceStateEventArgs
        {
            GigGossipNode = me,
            Source = source,
            State = state,
            Uri = uri
        });
    }

    public void OnLNDInvoiceStateChanged(GigGossipNode me, InvoiceStateChange invoice)
    {
        _gigGossipNodeEventSource.FireOnLNDInvoiceStateChanged(new LNDInvoiceStateChangedEventArgs
        {
            GigGossipNode = me,
            InvoiceStateChange = invoice,
        });
    }

    public void OnLNDPaymentStatusChanged(GigGossipNode me, PaymentStatusChanged payment)
    {
        _gigGossipNodeEventSource.FireOnLNDPaymentStatusChanged(new LNDPaymentStatusChangedEventArgs
        {
            GigGossipNode = me,
            PaymentStatusChanged = payment,
        });
    }

    public void OnLNDNewTransaction(GigGossipNode me, NewTransactionFound newTransaction)
    {
        _gigGossipNodeEventSource.FireOnLNDNewTransaction(new LNDNewTransactionEventArgs
        {
            GigGossipNode = me,
            NewTransactionFound = newTransaction,
        });
    }

    public void OnLNDPayoutStateChanged(GigGossipNode me, PayoutStateChanged payout)
    {
        _gigGossipNodeEventSource.FireOnLNDPayoutStateChanged(new LNDPayoutStateChangedEventArgs
        {
            GigGossipNode = me,
            PayoutStateChanged = payout,
        });
    }
}

