using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using ZXing.Net.Maui.Controls;
using GigMobile.Services;
using CryptoToolkit;

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
        serviceDescriptors.AddTransient(implementationFactory: WalletClientFactoryImplementation);
        serviceDescriptors.AddSingleton(implementationFactory: NodeFactoryImplementation);
    }

    private static GigLNDWalletAPIClient.swaggerClient WalletClientFactoryImplementation(IServiceProvider provider)
    {
        return new GigLNDWalletAPIClient.swaggerClient(GigGossipNodeConfig.GigWalletOpenApi, new HttpClient());
    }

    private static GigGossipNode NodeFactoryImplementation(IServiceProvider provider)
    {
        var node = new GigGossipNode(
            $"Filename={Path.Combine(FileSystem.AppDataDirectory, GigGossipNodeConfig.DatabaseFile)}",
            SecureDatabase.PrivateKey.AsECPrivKey(),
            GigGossipNodeConfig.NostrRelays,
            GigGossipNodeConfig.ChunkSize
        );

        var walletClient = provider.GetService<GigLNDWalletAPIClient.swaggerClient>();

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

