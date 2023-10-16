using Newtonsoft.Json;

namespace GigMobile.Services
{
    class SecureStorage
    {
        Dictionary<string, string> keyValuePairs = new();
        public async Task<string> GetAsync(string key)
        {
            if(keyValuePairs.ContainsKey(key))
                return keyValuePairs[key];
            return null;
        }

        public async Task SetAsync(string key, string value)
        {
            keyValuePairs[key] = value;
        }

        public static SecureStorage Default = new SecureStorage();
    }

	public class SecureDatabase
    {
        const string PRK = "WL_PR_K";

        const string UBM = "US_BM";

        const string TEN = "TR_EN";

        const string ISP = "IS_STP";

        public enum SetupStatus { Finished = 256, Enforcer = 0, Wallet, }

        public static string PrivateKey { get; private set; }

        public static async Task<string> GetPrivateKeyAsync()
		{
            PrivateKey = await SecureStorage.Default.GetAsync(PRK);
            return PrivateKey;
        }

        public static async Task SetPrivateKeyAsync(string key)
        {
            PrivateKey = key;
            await SecureStorage.Default.SetAsync(PRK, key);
        }

        public static async Task<bool> GetUseBiometricAsync()
        {
            var value = await SecureStorage.Default.GetAsync(UBM);
            if (!string.IsNullOrEmpty(value))
            {
                var key = await GetPrivateKeyAsync();
                var dc = JsonConvert.DeserializeObject<Dictionary<string,bool>>(value);
                if (dc.ContainsKey(key))
                    return dc[key];
            }
            return false;
        }

        public static async Task SetUseBiometricAsync(bool value)
        {
            var existingValue = await SecureStorage.Default.GetAsync(UBM);
            var key = await GetPrivateKeyAsync();

            Dictionary<string, bool> dc;
            if (!string.IsNullOrEmpty(existingValue))
                dc = JsonConvert.DeserializeObject<Dictionary<string, bool>>(existingValue);
            else
                dc = new Dictionary<string, bool> { { key, false } };
            dc[key] = value;

            await SecureStorage.Default.SetAsync(UBM, JsonConvert.SerializeObject(dc));
        }

        public static async Task<string[]> GetTrustEnforcersAsync()
        {
            var value = await SecureStorage.Default.GetAsync(TEN);
            if (!string.IsNullOrEmpty(value))
            {
                var key = await GetPrivateKeyAsync();
                var dc = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(value);
                if (dc.ContainsKey(key))
                    return dc[key];
            }
            return null;
        }

        public static async Task AddTrustEnforcersAsync(string value)
        {
            var existingValue = await SecureStorage.Default.GetAsync(TEN);
            var key = await GetPrivateKeyAsync();

            Dictionary<string, string[]> dc;
            if (!string.IsNullOrEmpty(existingValue))
                dc = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(existingValue);
            else
                dc = new Dictionary<string, string[]> { { key, null } };
            var oldValue = dc[key];
            var newValue = new List<string>();
            if (oldValue != null)
                foreach (var old in oldValue)
                    newValue.Add(old);
            newValue.Add(value);
            dc[key] = newValue.ToArray();

            await SecureStorage.Default.SetAsync(TEN, JsonConvert.SerializeObject(dc));
        }

        public static async Task<SetupStatus> GetGetSetupStatusAsync()
        {
            var value = await SecureStorage.Default.GetAsync(ISP);
            if (!string.IsNullOrEmpty(value))
            {
                var key = await GetPrivateKeyAsync();
                var dc = JsonConvert.DeserializeObject<Dictionary<string, SetupStatus>>(value);
                if (dc.ContainsKey(key))
                    return dc[key];
            }
            return 0;
        }

        public static async Task SetSetSetupStatusAsync(SetupStatus value)
        {
            var existingValue = await SecureStorage.Default.GetAsync(ISP);
            var key = await GetPrivateKeyAsync();

            Dictionary<string, SetupStatus> dc;
            if (!string.IsNullOrEmpty(existingValue))
                dc = JsonConvert.DeserializeObject<Dictionary<string, SetupStatus>>(existingValue);
            else
                dc = new Dictionary<string, SetupStatus> { { key, 0 } };
            dc[key] = value;

            await SecureStorage.Default.SetAsync(ISP, JsonConvert.SerializeObject(dc));
        }
    }
}

