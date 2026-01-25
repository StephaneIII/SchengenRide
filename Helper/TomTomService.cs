using System;
using System.Text.Json;
using SamkørselApp.Model;

namespace SamkørselApp.Helper
{
    public class TomTomService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public TomTomService()
        {
            _httpClient = new HttpClient();
            
            // Get API key from configuration
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
            
            _apiKey = config["TomTom:ApiKey"] ?? throw new InvalidOperationException("TomTom API key not found in configuration");
            _baseUrl = config["TomTom:BaseUrl"] ?? "https://api.tomtom.com";
        }

        public async Task<RouteCalculationResult?> CalculateRouteAsync(double startLat, double startLon, double endLat, double endLon)
        {
            try
            {
                // Try multiple strategies to handle coordinates that might not be exactly on roads
                RouteCalculationResult? result = null;
                
                // Strategy 1: Try exact coordinates with routeRepresentation=none for faster processing
                result = await TryCalculateRoute(startLat, startLon, endLat, endLon, "routeRepresentation=none");
                if (result != null) return result;
                
                // Strategy 2: Add tolerant routing options
                result = await TryCalculateRoute(startLat, startLon, endLat, endLon, "routeRepresentation=none&routingMode=tolerant");
                if (result != null) return result;
                
                // Strategy 3: Add avoid options to make routing more flexible
                result = await TryCalculateRoute(startLat, startLon, endLat, endLon, "routeRepresentation=none&avoid=unpavedRoads");
                if (result != null) return result;
                
                // Strategy 4: Use basic routing without additional parameters
                result = await TryCalculateRoute(startLat, startLon, endLat, endLon, "");
                if (result != null) return result;
                
                Console.WriteLine("All routing strategies failed");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in CalculateRouteAsync: {ex.Message}");
                return null;
            }
        }
        
        private async Task<RouteCalculationResult?> TryCalculateRoute(double startLat, double startLon, double endLat, double endLon, string additionalParams)
        {
            try
            {
                // Build TomTom API URL with invariant culture to ensure decimal points (not commas)
                string baseUrl = $"{_baseUrl}/routing/1/calculateRoute/{startLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{startLon.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{endLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{endLon.ToString(System.Globalization.CultureInfo.InvariantCulture)}/json?key={_apiKey}";
                
                string url = string.IsNullOrEmpty(additionalParams) ? baseUrl : $"{baseUrl}&{additionalParams}";
                
                // Debug: Print the URL to console
                Console.WriteLine($"Trying TomTom API URL: {url}");

                var response = await _httpClient.GetAsync(url);
                var jsonContent = await response.Content.ReadAsStringAsync();
                
                // Check for detailed errors in the response
                if (jsonContent.Contains("detailedError"))
                {
                    Console.WriteLine($"TomTom API Error Response: {jsonContent}");
                    return null;
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"HTTP Error: {response.StatusCode}");
                    return null;
                }

                var tomTomResponse = JsonSerializer.Deserialize<TomTomRouteResponse>(jsonContent);

                if (tomTomResponse?.routes?.Length > 0)
                {
                    var route = tomTomResponse.routes[0];
                    var summary = route.summary;

                    Console.WriteLine($"Route calculation successful with strategy: {additionalParams}");
                    return new RouteCalculationResult
                    {
                        DistanceKm = (decimal)Math.Round(summary.lengthInMeters / 1000.0, 2), // Convert meters to km
                        TravelTimeMinutes = Math.Round(summary.travelTimeInSeconds / 60.0, 0)
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in TryCalculateRoute: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // Helper classes for deserializing TomTom API response
    public class RouteCalculationResult
    {
        public decimal DistanceKm { get; set; }
        public double TravelTimeMinutes { get; set; }
    }

    public class TomTomRouteResponse
    {
        public TomTomRoute[]? routes { get; set; }
    }

    public class TomTomRoute
    {
        public TomTomRouteSummary summary { get; set; } = new();
    }

    public class TomTomRouteSummary
    {
        public int lengthInMeters { get; set; }
        public int travelTimeInSeconds { get; set; }
    }
}