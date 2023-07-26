using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using System.Text;
using NGigTaxiLib;

namespace GigWorkerTest;

public class MidTest
{
    public MidTest()
    {
    }

    public void Run()
    {

        int NUM_IN = 3;

        var ca = Cert.CreateCertificationAuthority("CA");
        var settlerPrivKey = Crypto.GeneratECPrivKey();
        var setter_certificate = ca.IssueCertificate(settlerPrivKey.CreateXOnlyPubKey(), "is_ok", true, DateTime.Now.AddDays(7), DateTime.Now.AddDays(-7));
        var settler = new Settler("ST", setter_certificate, settlerPrivKey, 12);

        var gigWorker = new GigWorker(ca, 1, settler);

        List<Gossiper> gossipers = new List<Gossiper>();

        for (int i = 0; i < NUM_IN; i++)
            gossipers.Add(new Gossiper( ca, 2, settler));

        gigWorker.ConnectTo(gossipers[0]);

        for (int i = 0; i < NUM_IN - 1; i++)
            for (int j = i + 1; j < NUM_IN; j++)
                gossipers[i].ConnectTo(gossipers[j]);

        var customer = new Customer(ca, 1, settler);
        customer.ConnectTo(gossipers[NUM_IN - 1]);


        customer.OnNewResponse += Customer_OnNewResponse;
        customer.OnResponseReady += Customer_OnResponseReady;

        gigWorker.Start();
        for (int i = 0; i < NUM_IN; i++)
            gossipers[i].Start();
        customer.Start();

        customer.Go();

        while (true)
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

