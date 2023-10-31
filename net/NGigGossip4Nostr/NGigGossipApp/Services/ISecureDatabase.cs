using GigMobile.Models;

namespace GigMobile.Services
{
    public interface ISecureDatabase
    {
        string PrivateKey { get; }

        Task<string> GetPrivateKeyAsync();
        Task SetPrivateKeyAsync(string key);
        Task<Dictionary<string, TrustEnforcer>> GetTrustEnforcersAsync();
        Task DeleteTrustEnforcersAsync(string key);
        Task CreateOrUpdateTrustEnforcersAsync(TrustEnforcer newTrustEnforcer);
        Task<SetupStatus> GetGetSetupStatusAsync();
        Task SetSetSetupStatusAsync(SetupStatus value);
        Task SetUseBiometricAsync(bool value);
        Task<bool> GetUseBiometricAsync();
        Task SetWalletDomain(string value);
        Task<string> GetWalletDomain();
    }
}