using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using ZXing.Net.Maui.Controls;
using GigMobile.Services;
using CryptoToolkit;
using NGigGossip4Nostr;
using GigLNDWalletAPIClient;
using System.Text;
using Sharpnado.Tabs;
using System.ComponentModel;

namespace GigMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .UseSkiaSharp(true)
            .UseSharpnadoTabs(false)
            .UseBarcodeReader();

#if ANDROID && DEBUG
        Platforms.Android.DangerousAndroidMessageHandlerEmitter.Register();
        Platforms.Android.DangerousTrustProvider.Register();
#endif
#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.RegisterServices();
        builder.Services.RegisterViewModels();

        return builder.Build();
    }

    public static void RegisterViewModels(this IServiceCollection serviceDescriptors)
    {
        serviceDescriptors.AddTransient<ViewModels.Profile.CreateProfileViewModel>();
        serviceDescriptors.AddTransient<ViewModels.Profile.LoginPrKeyViewModel>();
        serviceDescriptors.AddTransient<ViewModels.Profile.ProfileSetupViewModel>();
        serviceDescriptors.AddTransient<ViewModels.Profile.RecoverProfileViewModel>();

        serviceDescriptors.AddTransient<ViewModels.TrustEnforcers.AddTrEnfViewModel>();
        serviceDescriptors.AddTransient<ViewModels.TrustEnforcers.TrustEnforcersViewModel>();

        serviceDescriptors.AddTransient<ViewModels.Wallet.AddWalletViewModel>();
        serviceDescriptors.AddTransient<ViewModels.Wallet.WalletDetailsViewModel>();
        serviceDescriptors.AddTransient<ViewModels.Wallet.WithdrawBitcoinViewModel>();

        serviceDescriptors.AddTransient<ViewModels.Ride.Customer.CreateRideViewModel>();
    }

    public static void RegisterServices(this IServiceCollection serviceDescriptors)
    {
        serviceDescriptors.AddSingleton<BindedMvvm.INavigationService, BindedMvvm.NavigationService>();
        serviceDescriptors.AddSingleton(implementationFactory: NodeFactoryImplementation);
        serviceDescriptors.AddSingleton<IGigGossipNodeEventSource, GigGossipNodeEventSource>();
    }

    private static GigGossipNode NodeFactoryImplementation(IServiceProvider provider)
    {
        var node = new GigGossipNode(
            $"Filename={Path.Combine(FileSystem.AppDataDirectory, GigGossipNodeConfig.DatabaseFile)}",
            SecureDatabase.PrivateKey.AsECPrivKey(),
            GigGossipNodeConfig.NostrRelays,
            GigGossipNodeConfig.ChunkSize
        );

        var address = GigGossipNodeConfig.GigWalletOpenApi;

#if DEBUG
        if (DeviceInfo.Platform == DevicePlatform.Android)
            address = address.Replace("localhost", "10.0.2.2");

        HttpClient client = new(HttpsClientHandlerService.GetPlatformMessageHandler());
#else
        HttpClient client = new HttpClient();
#endif

        var walletClient = new GigLNDWalletAPIClient.swaggerClient(address, client);

        node.Init(
            GigGossipNodeConfig.Fanout,
            GigGossipNodeConfig.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(GigGossipNodeConfig.BroadcastConditionsTimeoutMs),
            GigGossipNodeConfig.BroadcastConditionsPowScheme,
            GigGossipNodeConfig.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(GigGossipNodeConfig.TimestampToleranceMs),
            TimeSpan.FromSeconds(GigGossipNodeConfig.InvoicePaymentTimeoutSec),
            walletClient);

        return node;
    }
}

public class NewResponseEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required ReplyPayload ReplyPayload;
    public required string ReplyInvoice;
    public required PayReq DecodedReplyInvoice;
    public required string NetworkInvoice;
    public required PayReq DecodedNetworkInvoice;
};


public interface IGigGossipNodeEventSource
{
    public event EventHandler<NewResponseEventArgs> OnNewResponse;
    public IGigGossipNodeEvents GetGigGossipNodeEvents();
}

public class GigGossipNodeEventSource : IGigGossipNodeEventSource
{
    public event EventHandler<NewResponseEventArgs> OnNewResponse;

    GigGossipNodeEvents gigGossipNodeEvents;

    public GigGossipNodeEventSource()
    {
        this.gigGossipNodeEvents = new GigGossipNodeEvents(this);
    }

    public void FireOnNewResponse(NewResponseEventArgs args)
    {
        OnNewResponse.Invoke(this,args);
    }

    public IGigGossipNodeEvents GetGigGossipNodeEvents()
    {
        return this.gigGossipNodeEvents;
    }
}

public class GigGossipNodeEvents : IGigGossipNodeEvents
{
    GigGossipNodeEventSource gigGossipNodeEventSource;

    public GigGossipNodeEvents(GigGossipNodeEventSource gigGossipNodeEventSource)
    {
        this.gigGossipNodeEventSource = gigGossipNodeEventSource;
    }

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(
            broadcastFrame.BroadcastPayload.SignedRequestPayload.Topic);

        if (taxiTopic != null)
        {
            /*
            me.AcceptBroadcast(peerPublicKey, broadcastFrame,
                new AcceptBroadcastResponse()
                {
                    Message = Encoding.Default.GetBytes(me.PublicKey),
                    Fee = 4321,
                    SettlerServiceUri = settlerUri,
                    MyCertificate = selectedCertificate
                });
            */
        }
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceAcceptedData iac)
    {
        await me.PayNetworkInvoiceAsync(iac);
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
    }

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        gigGossipNodeEventSource.FireOnNewResponse(new NewResponseEventArgs()
        {
            GigGossipNode = me,
            ReplyPayload = replyPayload,
            ReplyInvoice = replyInvoice,
            DecodedReplyInvoice = decodedReplyInvoice,
            NetworkInvoice = networkInvoice,
            DecodedNetworkInvoice = decodedNetworkInvoice,
        });
    }

    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
    {
        var message = Encoding.Default.GetString(Crypto.SymmetricDecrypt<byte[]>(
            key.AsBytes(),
            replyPayload.EncryptedReplyMessage));
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
    }
}

