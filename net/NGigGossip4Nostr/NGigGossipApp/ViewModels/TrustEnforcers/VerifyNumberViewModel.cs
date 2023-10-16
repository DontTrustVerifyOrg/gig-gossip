using System.Collections.Generic;
using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
    public class NewTrustEnforcer
    {
        public string Url { get; set; }
        public string PhoneNumber { get; set; }
    }

    public class VerifyNumberViewModel : BaseViewModel<NewTrustEnforcer>
    {
        private readonly GigGossipNode _gigGossipNode;

        private NewTrustEnforcer _newTrustEnforcer;

        private ICommand _submitCommand;
        public ICommand SubmitCommand => _submitCommand ??= new Command(async () => await SubmitAsync());

        public short? Code0 { get; set; }
        public short? Code1 { get; set; }
        public short? Code2 { get; set; }
        public short? Code3 { get; set; }
        public short? Code4 { get; set; }
        public short? Code5 { get; set; }

        public override void Prepare(NewTrustEnforcer data)
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
                var token = await _gigGossipNode.MakeSettlerAuthTokenAsync(GigGossipNodeConfig.SettlerOpenApi);
                var settlerClient = _gigGossipNode.SettlerSelector.GetSettlerClient(GigGossipNodeConfig.SettlerOpenApi);
                await settlerClient.VerifyChannelAsync(token, _gigGossipNode.PublicKey, "PhoneNumber", "SMS", _newTrustEnforcer.PhoneNumber);

                var settletCert = await settlerClient.IssueCertificateAsync(token, _gigGossipNode.PublicKey, new List<string> { "PhoneNumber" });
                var certificate = Crypto.DeserializeObject<Certificate>(settletCert);

                //save certificate for later => LookingDriverViewModel
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

