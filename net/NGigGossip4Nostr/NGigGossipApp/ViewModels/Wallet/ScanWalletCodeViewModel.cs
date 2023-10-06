using System;
using ZXing.Net.Maui;

namespace GigMobile.ViewModels.Wallet
{
    public class ScanWalletCodeViewModel : BaseViewModel
    {
        internal async void OnCodeDetected(BarcodeResult barcodeResult)
        {
            await NavigationService.NavigateBackAsync(barcodeResult.Value);
        }
    }
}

