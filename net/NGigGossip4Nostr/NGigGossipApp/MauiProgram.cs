using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using ZXing.Net.Maui.Controls;
using GigMobile.Services;
using CryptoToolkit;
using NGigGossip4Nostr;
using GigLNDWalletAPIClient;
using System.Text;

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
            .UseBarcodeReader();

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


        if (DeviceInfo.Platform == DevicePlatform.Android)
            address = address.Replace("localhost", "10.0.2.2");

        var walletClient = new GigLNDWalletAPIClient.swaggerClient(address, new HttpClient(HttpsClientHandlerService.GetPlatformMessageHandler()));

        node.Init(
            GigGossipNodeConfig.Fanout,
            GigGossipNodeConfig.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(GigGossipNodeConfig.BroadcastConditionsTimeoutMs),
            GigGossipNodeConfig.BroadcastConditionsPowScheme,
            GigGossipNodeConfig.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(GigGossipNodeConfig.TimestampToleranceMs),
            TimeSpan.FromSeconds(GigGossipNodeConfig.InvoicePaymentTimeoutSec),
            walletClient);

        node.Start(new GigGossipNodeEvents());

        return node;
    }
}

public class GigGossipNodeEvents : IGigGossipNodeEvents
{
    public GigGossipNodeEvents()
    {
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

    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceAcceptedData iac)
    {
        me.PayNetworkInvoice(iac);
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
    }

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        me.AcceptResponse(replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice);
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

