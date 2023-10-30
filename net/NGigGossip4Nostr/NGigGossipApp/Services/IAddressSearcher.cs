namespace GigMobile.Services
{
    public interface IAddressSearcher
    {
        Task<Place[]> GetAddressAsync(string query, string city, string country, CancellationToken ct);
    }
}