using System.Windows.Input;
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

        public short[] Code { get; set; } = new short[6];

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

            var settlerToken = await _gigGossipNode.MakeSettlerAuthTokenAsync(GigGossipNodeConfig.SettlerOpenApi);
            await _gigGossipNode.SettlerSelector.GetSettlerClient(GigGossipNodeConfig.SettlerOpenApi).VerifyChannelAsync(settlerToken, _gigGossipNode.PublicKey, "PhoneNumber", "SMS", _newTrustEnforcer.PhoneNumber);
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

