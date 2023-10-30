using System.Security.Cryptography;
using System.Text;
using GigMobile.Models;
using Newtonsoft.Json;

namespace GigMobile.Services
{
    public enum SetupStatus { Finished = 256, Enforcer = 0, Wallet, }

    public class UserData
    {
        public UserData(bool useBiometric, string walletDomain, Dictionary<string, TrustEnforcer> trustEnforcers, SetupStatus status)
        {
            UseBiometric = useBiometric;
            WalletDomain = walletDomain;
            TrustEnforcers = trustEnforcers;
            Status = status;
        }

        public bool UseBiometric { get; set; }
        public string WalletDomain { get; set; }
        public Dictionary<string, TrustEnforcer> TrustEnforcers { get; set; }
        public SetupStatus Status { get; set; }
    }

    public class SecureDatabase : ISecureDatabase
    {
        private const string PRK = "SAVED_USER";

        private string _secureKey;

        public string PrivateKey { get; private set; }

        public async Task<string> GetPrivateKeyAsync()
        {
            PrivateKey = await SecureStorage.Default.GetAsync(PRK);
            _secureKey = GetHashString(PrivateKey);

            return PrivateKey;
        }

        public async Task SetPrivateKeyAsync(string key)
        {
            PrivateKey = key;
            _secureKey = GetHashString(PrivateKey);

            await SecureStorage.Default.SetAsync(PRK, key);
        }

        public async Task<bool> GetUseBiometricAsync()
        {
            var value = await SecureStorage.Default.GetAsync(_secureKey);
            if (!string.IsNullOrEmpty(value))
            {
                var data = JsonConvert.DeserializeObject<UserData>(value);
                return data.UseBiometric;
            }
            return false;
        }

        public async Task SetUseBiometricAsync(bool value)
        {
            var stringData = await SecureStorage.Default.GetAsync(_secureKey);
            UserData data;
            if (!string.IsNullOrEmpty(stringData))
            {
                data = JsonConvert.DeserializeObject<UserData>(stringData);
                data.UseBiometric = value;
            }
            else
                data = new UserData(value, null, null, SetupStatus.Wallet);

            await SecureStorage.Default.SetAsync(_secureKey, JsonConvert.SerializeObject(data));
        }


        public async Task<string> GetWalletDomain()
        {
            var value = await SecureStorage.Default.GetAsync(_secureKey);
            if (!string.IsNullOrEmpty(value))
            {
                var data = JsonConvert.DeserializeObject<UserData>(value);
                return data.WalletDomain;
            }
            return null;
        }

        public async Task SetWalletDomain(string value)
        {
            var stringData = await SecureStorage.Default.GetAsync(_secureKey);

            UserData data = JsonConvert.DeserializeObject<UserData>(stringData);

            data.WalletDomain = value;

            await SecureStorage.Default.SetAsync(_secureKey, JsonConvert.SerializeObject(data));
        }

        public async Task<Dictionary<string, TrustEnforcer>> GetTrustEnforcersAsync()
        {
            var value = await SecureStorage.Default.GetAsync(_secureKey);
            if (!string.IsNullOrEmpty(value))
            {
                var data = JsonConvert.DeserializeObject<UserData>(value);
                return data.TrustEnforcers;
            }
            return null;
        }

        public async Task AddTrustEnforcersAsync(TrustEnforcer newTrustEnforcer)
        {
            var stringData = await SecureStorage.Default.GetAsync(_secureKey);

            UserData data = JsonConvert.DeserializeObject<UserData>(stringData);

            data.TrustEnforcers ??= new Dictionary<string, TrustEnforcer>();
            data.TrustEnforcers.Add(newTrustEnforcer.Uri, newTrustEnforcer);

            await SecureStorage.Default.SetAsync(_secureKey, JsonConvert.SerializeObject(data));
        }

        public async Task<SetupStatus> GetGetSetupStatusAsync()
        {
            var value = await SecureStorage.Default.GetAsync(_secureKey);
            if (!string.IsNullOrEmpty(value))
            {
                var data = JsonConvert.DeserializeObject<UserData>(value);
                return data.Status;
            }
            return 0;
        }

        public async Task SetSetSetupStatusAsync(SetupStatus value)
        {
            var stringData = await SecureStorage.Default.GetAsync(_secureKey);

            UserData data = JsonConvert.DeserializeObject<UserData>(stringData);

            data.Status = value;

            await SecureStorage.Default.SetAsync(_secureKey, JsonConvert.SerializeObject(data));
        }


        public static string GetHashString(string inputString)
        {
            StringBuilder sb = new();

            foreach (byte b in SHA256.HashData(Encoding.UTF8.GetBytes(inputString)))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }
    }
}

