namespace xOTACompanion.Services
{
    /// <summary>
    /// Maidenhead grid square utilities.
    /// Ported from GreenLogger_New with no changes to the maths.
    /// </summary>
    public static class MaidenheadService
    {
        public static (double latitude, double longitude) LocatorToCoordinates(string locator)
        {
            if (string.IsNullOrEmpty(locator) || locator.Length < 4)
                return (0, 0);

            locator = locator.ToUpperInvariant();

            double longitude = (locator[0] - 'A') * 20.0 - 180.0;
            double latitude  = (locator[1] - 'A') * 10.0 - 90.0;
            longitude += (locator[2] - '0') * 2.0;
            latitude  += (locator[3] - '0') * 1.0;
            longitude += 1.0;
            latitude  += 0.5;

            if (locator.Length >= 6)
            {
                double subsqLon = (locator[4] - 'A') * (2.0 / 24.0);
                double subsqLat = (locator[5] - 'A') * (1.0 / 24.0);
                longitude = (locator[0] - 'A') * 20.0 - 180.0
                          + (locator[2] - '0') * 2.0
                          + subsqLon + (2.0 / 24.0) / 2.0;
                latitude  = (locator[1] - 'A') * 10.0 - 90.0
                          + (locator[3] - '0') * 1.0
                          + subsqLat + (1.0 / 24.0) / 2.0;
            }

            return (latitude, longitude);
        }

        public static double CalculateDistance(string locator1, string locator2)
        {
            var (lat1, lon1) = LocatorToCoordinates(locator1);
            var (lat2, lon2) = LocatorToCoordinates(locator2);
            return CalculateDistanceFromCoords(lat1, lon1, lat2, lon2);
        }

        public static double CalculateDistanceFromCoords(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = Deg2Rad(lat2 - lat1);
            double dLon = Deg2Rad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2))
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double Deg2Rad(double d) => d * Math.PI / 180.0;
    }
}
