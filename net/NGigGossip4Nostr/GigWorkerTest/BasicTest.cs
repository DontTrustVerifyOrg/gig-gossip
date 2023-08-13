using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using System.Text;
using CryptoToolkit;
namespace GigWorkerTest;

//Crypto.GeneratECPrivKey(), new[] { "ws://127.0.0.1:6969" }

public class BasicTest
{
    public BasicTest()
    {
    }

    public void Run()
    {

        var ca = Cert.CreateCertificationAuthority("CA");
        var settlerPrivKey = Crypto.GeneratECPrivKey();
        var setter_certificate = ca.IssueCertificate(settlerPrivKey.CreateXOnlyPubKey(), "is_ok", true, DateTime.Now.AddDays(7), DateTime.Now.AddDays(-7));
        var settler = new Settler("ST", setter_certificate, settlerPrivKey, 12);

        var gigWorker = new GigWorker(ca, 1, settler);
        var customer = new Customer(ca, 1, settler);

        gigWorker.ConnectTo(customer);

        customer.OnNewResponse += Customer_OnNewResponse;
        customer.OnResponseReady += Customer_OnResponseReady;

        gigWorker.Start();
        customer.Start();

        customer.Go();

        while(true)
        {
            Thread.Sleep(1000);
        }

    }


    private void Customer_OnNewResponse(object? sender, ResponseEventArgs e)
    {
        (sender as GigGossipNode).AcceptResponse(e.payload, e.network_invoice);
    }

    private void Customer_OnResponseReady(object? sender, ResponseEventArgs e)
    {
        var message = (byte[])Crypto.SymmetricDecrypt(e.network_invoice.Preimage, e.payload.EncryptedReplyMessage);
        Trace.TraceInformation(Encoding.Default.GetString(message));
    }
}

