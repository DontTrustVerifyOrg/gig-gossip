using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Models;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class AddTrEnfViewModel : BaseViewModel<bool>
    {
        private readonly ISecureDatabase _secureDatabase;
        private readonly GigGossipNode _gigGossipNode;
        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(async () => await OpenAddTrEnfAsync());

        private ICommand _skipCommand;
        public ICommand SkipCommand => _skipCommand ??= new Command(async () => await SkipAsync());

        public bool FromSetup { get; private set; }

        public string Url { get; set; }
        public string PhoneCode { get; set; }
        public string PhoneNumber { get; set; }

        public AddTrEnfViewModel(ISecureDatabase secureDatabase, GigGossipNode gigGossipNode)
        {
            _secureDatabase = secureDatabase;
            _gigGossipNode = gigGossipNode;
        }

        public override void Prepare(bool data)
        {
            FromSetup = data;
        }

        private async Task SkipAsync()
        {
            if (FromSetup)
            {
                await AddDefaultTrustEnforcerAsync();
                await _secureDatabase.SetSetSetupStatusAsync(SetupStatus.Wallet);
                await NavigationService.NavigateAsync<Wallet.AddWalletViewModel>();
            }
            else
                await NavigationService.NavigateBackAsync();
        }

        private async Task AddDefaultTrustEnforcerAsync()
        {
            try
            {
                Url = GigGossipNodeConfig.SettlerOpenApi.ToString();
                var newTrustEnforcer = new TrustEnforcer { Name = "LocalHost", Uri = Url, PhoneNumber = $"+{PhoneCode} {PhoneNumber}" };
                var token = await _gigGossipNode.MakeSettlerAuthTokenAsync(new Uri(newTrustEnforcer.Uri));
                var settlerClient = _gigGossipNode.SettlerSelector.GetSettlerClient(new Uri(newTrustEnforcer.Uri));
                await settlerClient.VerifyChannelAsync(token, _gigGossipNode.PublicKey, "PhoneNumber", "SMS", newTrustEnforcer.PhoneNumber);
                var retries = await settlerClient.SubmitChannelSecretAsync(token, _gigGossipNode.PublicKey, "PhoneNumber", "SMS", newTrustEnforcer.PhoneNumber, "MOCK");
                var settletCert = await settlerClient.IssueCertificateAsync(token, _gigGossipNode.PublicKey, new List<string> { "PhoneNumber" });
                newTrustEnforcer.Certificate = Crypto.DeserializeObject<Certificate>(settletCert);
                await _secureDatabase.CreateOrUpdateTrustEnforcersAsync(newTrustEnforcer);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private async Task OpenAddTrEnfAsync()
        {
            if (!string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(PhoneCode) && !string.IsNullOrEmpty(PhoneNumber))
            {
                //TODO MOCK URL
                Url = GigGossipNodeConfig.SettlerOpenApi.ToString();
                await NavigationService.NavigateAsync<VerifyNumberViewModel, TrustEnforcer>(new TrustEnforcer { Name = "LocalHost", Uri = Url, PhoneNumber = $"+{PhoneCode} {PhoneNumber}" }, onClosed: async (x) => await OnAddedClosed());
            }
        }

        private async Task OnAddedClosed()
        {
            if (FromSetup)
            {
                await _secureDatabase.SetSetSetupStatusAsync(SetupStatus.Wallet);
                await NavigationService.NavigateAsync<Wallet.AddWalletViewModel>();
            }
            else
                await NavigationService.NavigateBackAsync();
        }
    }
}

