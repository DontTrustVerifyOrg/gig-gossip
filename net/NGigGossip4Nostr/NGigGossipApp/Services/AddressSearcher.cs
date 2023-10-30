using System;
using Newtonsoft.Json;

namespace GigMobile.Services
{
    public class AddressSearcher : IAddressSearcher
    {
        private readonly HttpClient _httpClient;

        public AddressSearcher()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://nominatim.openstreetmap.org")
            };
        }

        public async Task<Place[]> GetAddressAsync(string query, string city, string country, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(query))
                    return Array.Empty<Place>();

                var response = await _httpClient.GetAsync($"/search?street={query.Replace(' ', '+')}&city={city}&country={country}&format=json", ct);
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Console.WriteLine(response.StatusCode);
                    return Array.Empty<Place>();
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync(ct);
                    return JsonConvert.DeserializeObject<Place[]>(responseBody);
                }
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return Array.Empty<Place>();
            }
        }
    }

    public class AddressDetails
    {
        [JsonProperty("house_number")]
        public string HouseNumber { get; set; }

        [JsonProperty("road")]
        public string Road { get; set; }

        [JsonProperty("city_block")]
        public string CityBlock { get; set; }

        [JsonProperty("quarter")]
        public string Quarter { get; set; }

        [JsonProperty("suburb")]
        public string Suburb { get; set; }

        [JsonProperty("city")]
        public string City { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("ISO3166-2-lvl4")]
        public string ISO31662Lvl4 { get; set; }

        [JsonProperty("postcode")]
        public string Postcode { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("country_code")]
        public string CountryCode { get; set; }
    }

    public class Place
    {
        [JsonProperty("place_id")]
        public int PlaceId { get; set; }

        [JsonProperty("licence")]
        public string Licence { get; set; }

        [JsonProperty("osm_type")]
        public string OsmType { get; set; }

        [JsonProperty("osm_id")]
        public long OsmId { get; set; }

        [JsonProperty("lat")]
        public string Lat { get; set; }

        [JsonProperty("lon")]
        public string Lon { get; set; }

        [JsonProperty("class")]
        public string Class { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("place_rank")]
        public int PlaceRank { get; set; }

        [JsonProperty("importance")]
        public double Importance { get; set; }

        [JsonProperty("addresstype")]
        public string Addresstype { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("address")]
        public AddressDetails Address { get; set; }

        [JsonProperty("boundingbox")]
        public List<string> Boundingbox { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

}

