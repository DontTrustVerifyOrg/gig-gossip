using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Models;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
    public class VerifyNumberViewModel : BaseViewModel<TrustEnforcer>
    {
        private readonly GigGossipNode _gigGossipNode;

        private TrustEnforcer _newTrustEnforcer;

        private ICommand _submitCommand;
        public ICommand SubmitCommand => _submitCommand ??= new Command(async () => await SubmitAsync());

        public short? Code0 { get; set; }
        public short? Code1 { get; set; }
        public short? Code2 { get; set; }
        public short? Code3 { get; set; }
        public short? Code4 { get; set; }
        public short? Code5 { get; set; }

        public override void Prepare(TrustEnforcer data)
        {
            _newTrustEnforcer = data;
        }

        public VerifyNumberViewModel(GigGossipNode gigGossipNode)
        {
            _gigGossipNode = gigGossipNode;
        }

        public async override Task Initialize()
        {
            await base.Initialize();

            try
            {
                var token = await _gigGossipNode.MakeSettlerAuthTokenAsync(new Uri(_newTrustEnforcer.Uri));
                var settlerClient = _gigGossipNode.SettlerSelector.GetSettlerClient(new Uri(_newTrustEnforcer.Uri));
                await settlerClient.VerifyChannelAsync(token, _gigGossipNode.PublicKey, "PhoneNumber", "SMS", _newTrustEnforcer.PhoneNumber);

                var settletCert = await settlerClient.IssueCertificateAsync(token, _gigGossipNode.PublicKey, new List<string> { "PhoneNumber" });
                _newTrustEnforcer.Certificate = Crypto.DeserializeObject<Certificate>(settletCert);

                await SecureDatabase.AddTrustEnforcersAsync(_newTrustEnforcer);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }


        private async Task SubmitAsync()
        {
            /*TODO
            var code = string.Join("", Code);
            var success = PAWEL_API.VerifySmsCode (code);
            if (success)
            {
                var privateKey = await SecureDatabase.GetPrivateKeyAsync();
                var publicKey = privateKey.AsECPrivKey().CreatePubKey()
                var newTrustEnf = PAWE_API.AddTrustEnf(_newTrustEnforcer.Url, _newTrustEnforcer.PhoneNumber, publicKey);
                await SecureDatabase.AddTrustEnforcersAsync(_newTrustEnforcer.Url);
            }
            */

            await NavigationService.NavigateBackAsync();
        }
    }
}

