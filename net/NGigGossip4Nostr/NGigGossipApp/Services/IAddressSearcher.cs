namespace GigMobile.Services
{
    public interface IAddressSearcher
    {
        Task<Place[]> GetAddressAsync(string query, CancellationToken ct);
    }
}