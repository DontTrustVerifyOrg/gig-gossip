using GigMobile.ViewModels.Wallet;

namespace GigMobile.Pages.Wallet;

public partial class ScanWalletCodePage : BasePage<ScanWalletCodeViewModel>
{
	public ScanWalletCodePage()
	{
		InitializeComponent();
	}

    void CameraBarcodeReaderView_BarcodesDetected(System.Object sender, ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
		ViewModel.OnCodeDetected(e.Results[0]);
    }
}
