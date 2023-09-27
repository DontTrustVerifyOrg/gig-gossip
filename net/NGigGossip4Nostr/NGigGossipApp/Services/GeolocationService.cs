namespace GigMobile.Services
{
	public static class GeolocationService
	{
        public static async Task<Location> GetCachedLocation()
        {
            try
            {
                return await Geolocation.Default.GetLastKnownLocationAsync(); ;
            }
            catch (FeatureNotSupportedException fnsEx)
            {
                // Handle not supported on device exception
            }
            catch (FeatureNotEnabledException fneEx)
            {
                // Handle not enabled on device exception
            }
            catch (PermissionException pEx)
            {
                var a = pEx;
            }
            catch (Exception ex)
            {
                // Unable to get location
            }

            return null;
        }
    }
}

