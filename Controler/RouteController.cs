using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;
using System.Security.Cryptography;
using System.Text;
using SamkørselApp.Model;
using RouteModel = SamkørselApp.Model.Route;

namespace SamkørselApp.Controllers
{
    public class RouteController : Controller
    {
        private readonly string connectionString;

        public RouteController()
        {
            ConnectionStringGetter connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        public IActionResult Index()
        {
            string? uid = HttpContext.Session.GetString("UID");
            
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            List<Vehicle> vehicles = new List<Vehicle>();
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT VehicleID, UID, Brand, Model, Color, PlateNumber, ComfortLevel FROM Vehicle WHERE UID = @UID";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UID", int.Parse(uid));

                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    vehicles.Add(new Vehicle(
                        (int)reader["VehicleID"],
                        (int)reader["UID"],
                        reader["Brand"]?.ToString() ?? "",
                        reader["Model"]?.ToString() ?? "",
                        reader["Color"]?.ToString() ?? "",
                        reader["PlateNumber"]?.ToString() ?? "",
                        reader["ComfortLevel"]?.ToString() ?? ""
                    ));
                }
            }

            ViewBag.Vehicles = vehicles;
            ViewBag.Itinerary = new List<City>();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(DateTime Departure, DateTime? Arrival, int AvailableSeats, decimal PricePerSeat, int VehicleId, string? Description, List<ItineraryStopInput> Stops)
        {
            string? uid = HttpContext.Session.GetString("UID");

            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            // Validate we have at least 2 stops (start and end)
            if (Stops == null || Stops.Count < 2)
            {
                TempData["Error"] = "You need at least 2 stops (start and end city).";
                return RedirectToAction("Index");
            }

            // Sort stops by StopOrder and get first/last city
            var orderedStops = Stops.OrderBy(s => s.StopOrder).ToList();
            int startCityID = orderedStops.First().CityID;
            int endCityID = orderedStops.Last().CityID;

            // Get city coordinates for TomTom API call
            City? startCity = null;
            City? endCity = null;
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                
                // Get start city coordinates
                string cityQuery = "SELECT CityID, CityName, CityXCoord, CityYCoord FROM City WHERE CityID = @CityID";
                using (SqlCommand command = new SqlCommand(cityQuery, connection))
                {
                    command.Parameters.AddWithValue("@CityID", startCityID);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            startCity = new City(
                                (int)reader["CityID"],
                                reader["CityName"]?.ToString() ?? "",
                                (double)reader["CityXCoord"],
                                (double)reader["CityYCoord"]
                            );
                        }
                    }
                }
                
                // Get end city coordinates
                using (SqlCommand command = new SqlCommand(cityQuery, connection))
                {
                    command.Parameters.AddWithValue("@CityID", endCityID);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            endCity = new City(
                                (int)reader["CityID"],
                                reader["CityName"]?.ToString() ?? "",
                                (double)reader["CityXCoord"],
                                (double)reader["CityYCoord"]
                            );
                        }
                    }
                }
            }

            // Calculate route distance and time using TomTom API
            decimal? distanceKm = null;
            int? travelTimeMinutes = null;
            
            if (startCity != null && endCity != null)
            {
                using var tomTomService = new TomTomService();
                var routeInfo = await tomTomService.CalculateRouteAsync(
                    startCity.CityYCoord, startCity.CityXCoord, // Note: TomTom expects lat,lon
                    endCity.CityYCoord, endCity.CityXCoord
                );
                
                if (routeInfo != null)
                {
                    distanceKm = routeInfo.DistanceKm;
                    travelTimeMinutes = (int)routeInfo.TravelTimeMinutes;
                }
            }

            int routeID;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                using var transaction = connection.BeginTransaction();

                try
                {
                    // 1. Insert the Route
                    string routeQuery = @"
                        INSERT INTO Route (UID, StartCityID, EndCityID, Departure, Arrival, AvailableSeats, PricePerSeat, VehicleID, Description, Status, DistanceKm, ExpectedTravelTimeMinutes)
                        OUTPUT INSERTED.RouteID
                        VALUES (@UID, @StartCityID, @EndCityID, @Departure, @Arrival, @AvailableSeats, @PricePerSeat, @VehicleID, @Description, @Status, @DistanceKm, @ExpectedTravelTimeMinutes)";

                    using (SqlCommand routeCommand = new SqlCommand(routeQuery, connection, transaction))
                    {
                        routeCommand.Parameters.AddWithValue("@UID", int.Parse(uid));
                        routeCommand.Parameters.AddWithValue("@StartCityID", startCityID);
                        routeCommand.Parameters.AddWithValue("@EndCityID", endCityID);
                        routeCommand.Parameters.AddWithValue("@Departure", Departure);
                        routeCommand.Parameters.AddWithValue("@Arrival", (object?)Arrival ?? DBNull.Value);
                        routeCommand.Parameters.AddWithValue("@AvailableSeats", AvailableSeats);
                        routeCommand.Parameters.AddWithValue("@PricePerSeat", PricePerSeat);
                        routeCommand.Parameters.AddWithValue("@VehicleID", VehicleId);
                        routeCommand.Parameters.AddWithValue("@Description", (object?)Description ?? DBNull.Value);
                        routeCommand.Parameters.AddWithValue("@Status", "Active");
                        routeCommand.Parameters.AddWithValue("@DistanceKm", (object?)distanceKm ?? DBNull.Value);
                        routeCommand.Parameters.AddWithValue("@ExpectedTravelTimeMinutes", (object?)travelTimeMinutes ?? DBNull.Value);

                        routeID = (int)routeCommand.ExecuteScalar();
                    }

                    // 2. Insert all Itinerary stops
                    string itineraryQuery = @"
                        INSERT INTO Itinerary (RouteID, CityID, StopOrder, MinArrivalTime, MaximalDelay)
                        VALUES (@RouteID, @CityID, @StopOrder, @MinArrivalTime, @MaximalDelay)";

                    foreach (var stop in orderedStops)
                    {
                        using (SqlCommand itineraryCommand = new SqlCommand(itineraryQuery, connection, transaction))
                        {
                            itineraryCommand.Parameters.AddWithValue("@RouteID", routeID);
                            itineraryCommand.Parameters.AddWithValue("@CityID", stop.CityID);
                            itineraryCommand.Parameters.AddWithValue("@StopOrder", stop.StopOrder);
                            itineraryCommand.Parameters.AddWithValue("@MinArrivalTime", (object?)stop.MinArrivalTime ?? DBNull.Value);
                            itineraryCommand.Parameters.AddWithValue("@MaximalDelay", (object?)stop.MaximalDelay ?? DBNull.Value);

                            itineraryCommand.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();

                    TempData["Success"] = "Route created successfully!";
                    return RedirectToAction("Index", "Home"); 
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["Error"] = "Error creating route: " + ex.Message;
                    return RedirectToAction("Index");
                }
            }
        }

        public IActionResult GetCitiesbyname(string name)
        {
            List<City> cities = new List<City>();
            
            if (string.IsNullOrEmpty(name))
            {
                return Json(cities);
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT TOP(6) [CityID],[CityName],[CityXCoord],[CityYCoord] FROM City WHERE CityName LIKE '%' + @name + '%'";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@name", name);

                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cities.Add(new City(
                        (int)reader["CityID"], 
                        reader["CityName"]?.ToString() ?? "", 
                        (double)reader["CityXCoord"], 
                        (double)reader["CityYCoord"]
                    ));
                }
            }
            return Json(cities);
        }

        [HttpPost]
        public async Task<IActionResult> CalculateAutomaticTimes([FromBody] AutoTimingRequest request)
        {
            try
            {
                if (request?.CityIds == null || request.CityIds.Count < 2)
                {
                    return Json(new { success = false, error = "At least 2 cities required" });
                }

                // Get cities with coordinates
                var cities = new List<City>();
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    foreach (int cityId in request.CityIds)
                    {
                        string query = "SELECT CityID, CityName, CityXCoord, CityYCoord FROM City WHERE CityID = @CityID";
                        using (SqlCommand command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@CityID", cityId);
                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    cities.Add(new City(
                                        (int)reader["CityID"],
                                        reader["CityName"].ToString() ?? "",
                                        (double)reader["CityXCoord"],
                                        (double)reader["CityYCoord"]
                                    ));
                                }
                            }
                        }
                    }
                }

                if (cities.Count != request.CityIds.Count)
                {
                    return Json(new { success = false, error = "Some cities not found" });
                }

                // Parse start time from request
                DateTime currentTime = DateTime.Now;
                if (!string.IsNullOrEmpty(request.StartTime) && DateTime.TryParse(request.StartTime, out DateTime startTime))
                {
                    currentTime = startTime;
                }

                // Parse end time if provided
                DateTime? endTime = null;
                if (!string.IsNullOrEmpty(request.EndTime) && DateTime.TryParse(request.EndTime, out DateTime parsedEndTime))
                {
                    endTime = parsedEndTime;
                }

                // Calculate times using TomTom
                var results = new List<dynamic>();

                using (var tomTomService = new TomTomService())
                {
                    for (int i = 0; i < cities.Count; i++)
                    {
                        if (i == 0)
                        {
                            // First city - use departure time from step 1
                            results.Add(new
                            {
                                cityId = cities[i].CityID,
                                minArrivalTime = currentTime.ToString("yyyy-MM-ddTHH:mm"),
                                travelTimeFromPrevious = 0
                            });
                        }
                        else if (i == cities.Count - 1 && endTime.HasValue)
                        {
                            // Last city - use arrival time from step 1 if provided
                            results.Add(new
                            {
                                cityId = cities[i].CityID,
                                minArrivalTime = endTime.Value.ToString("yyyy-MM-ddTHH:mm"),
                                travelTimeFromPrevious = 0
                            });
                        }
                        else
                        {
                            // Calculate travel time from previous city
                            var routeResult = await tomTomService.CalculateRouteAsync(
                                cities[i - 1].CityYCoord, // Lat
                                cities[i - 1].CityXCoord, // Lon
                                cities[i].CityYCoord,      // Lat
                                cities[i].CityXCoord       // Lon
                            );

                            double travelTimeMinutes = routeResult?.TravelTimeMinutes ?? 60; // Default 1 hour if API fails
                            currentTime = currentTime.AddMinutes(travelTimeMinutes);

                            // For the last city without end time, use calculated time
                            // For middle cities, use calculated time
                            results.Add(new
                            {
                                cityId = cities[i].CityID,
                                minArrivalTime = currentTime.ToString("yyyy-MM-ddTHH:mm"),
                                travelTimeFromPrevious = Math.Round(travelTimeMinutes, 0)
                            });
                        }
                    }
                }

                return Json(new { success = true, results });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }


        [HttpGet("RouteInfo/id/{id}/view")]
        public IActionResult RouteInfoView(int id)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    // Get route details with driver info
                    string routeQuery = @"
                        SELECT r.RouteID, r.UID, r.StartCityID, r.EndCityID, r.Departure, r.Arrival, 
                               r.AvailableSeats, r.PricePerSeat, r.VehicleID, r.Description, r.Status,
                               r.DistanceKm, r.ExpectedTravelTimeMinutes,
                               sc.CityName as StartCityName, sc.CityXCoord as StartCityX, sc.CityYCoord as StartCityY,
                               ec.CityName as EndCityName, ec.CityXCoord as EndCityX, ec.CityYCoord as EndCityY,
                               u.UserName, u.Email, u.Phone, u.ProfilePictureURL, u.Rating, u.JoinDate
                        FROM Route r
                        LEFT JOIN City sc ON r.StartCityID = sc.CityID
                        LEFT JOIN City ec ON r.EndCityID = ec.CityID
                        LEFT JOIN [User] u ON r.UID = u.UID
                        WHERE r.RouteID = @RouteID";

                    RouteModel? route = null;
                    User? driver = null;

                    using (SqlCommand command = new SqlCommand(routeQuery, connection))
                    {
                        command.Parameters.AddWithValue("@RouteID", id);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                // Create cities
                                City startCity = new City(
                                    (int)reader["StartCityID"],
                                    reader["StartCityName"]?.ToString() ?? "",
                                    reader["StartCityX"] != DBNull.Value ? (double)reader["StartCityX"] : 0,
                                    reader["StartCityY"] != DBNull.Value ? (double)reader["StartCityY"] : 0
                                );

                                City endCity = new City(
                                    (int)reader["EndCityID"],
                                    reader["EndCityName"]?.ToString() ?? "",
                                    reader["EndCityX"] != DBNull.Value ? (double)reader["EndCityX"] : 0,
                                    reader["EndCityY"] != DBNull.Value ? (double)reader["EndCityY"] : 0
                                );

                                // Create driver
                                driver = new User(
                                    (int)reader["UID"],
                                    reader["UserName"]?.ToString() ?? "",
                                    "", // Password not exposed
                                    reader["Email"]?.ToString() ?? "",
                                    reader["Phone"]?.ToString(),
                                    reader["ProfilePictureURL"]?.ToString(),
                                    reader["Rating"] != DBNull.Value ? (double?)reader["Rating"] : null,
                                    reader["JoinDate"] != DBNull.Value ? (DateTime)reader["JoinDate"] : DateTime.Now
                                );

                                route = new RouteModel(
                                    (int)reader["RouteID"],
                                    (int)reader["UID"],
                                    (int)reader["StartCityID"],
                                    startCity,
                                    (int)reader["EndCityID"],
                                    endCity,
                                    (DateTime)reader["Departure"],
                                    reader["Arrival"] != DBNull.Value ? (DateTime?)reader["Arrival"] : null,
                                    (int)reader["AvailableSeats"],
                                    (decimal)reader["PricePerSeat"],
                                    reader["VehicleID"] != DBNull.Value ? (int?)reader["VehicleID"] : null,
                                    reader["Description"]?.ToString(),
                                    reader["Status"]?.ToString() ?? "Active",
                                    reader["DistanceKm"] != DBNull.Value ? (decimal?)reader["DistanceKm"] : null,
                                    reader["ExpectedTravelTimeMinutes"] != DBNull.Value ? (int?)reader["ExpectedTravelTimeMinutes"] : null,
                                    new List<Itinerary>()
                                );
                            }
                            else
                            {
                                TempData["Error"] = $"Route with ID {id} not found.";
                                return RedirectToAction("Index");
                            }
                        }
                    }

                    // Get itinerary details
                    string itineraryQuery = @"
                        SELECT i.RouteID, i.CityID, i.StopOrder, i.MinArrivalTime, i.MaximalDelay,
                               c.CityName, c.CityXCoord, c.CityYCoord
                        FROM Itinerary i
                        LEFT JOIN City c ON i.CityID = c.CityID
                        WHERE i.RouteID = @RouteID
                        ORDER BY i.StopOrder";

                    using (SqlCommand command = new SqlCommand(itineraryQuery, connection))
                    {
                        command.Parameters.AddWithValue("@RouteID", id);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                City city = new City(
                                    (int)reader["CityID"],
                                    reader["CityName"]?.ToString() ?? "",
                                    reader["CityXCoord"] != DBNull.Value ? (double)reader["CityXCoord"] : 0,
                                    reader["CityYCoord"] != DBNull.Value ? (double)reader["CityYCoord"] : 0
                                );

                                Itinerary itinerary = new Itinerary(
                                    (int)reader["RouteID"],
                                    city,
                                    (int)reader["StopOrder"],
                                    reader["MinArrivalTime"] != DBNull.Value ? (DateTime?)reader["MinArrivalTime"] : null,
                                    reader["MaximalDelay"] != DBNull.Value ? (double?)reader["MaximalDelay"] : null
                                );

                                route.Itineraries.Add(itinerary);
                            }
                        }
                    }

                    // Get vehicle details if available
                    Vehicle? vehicle = null;
                    if (route.VehicleID.HasValue)
                    {
                        string vehicleQuery = @"
                            SELECT VehicleID, UID, Brand, Model, Color, PlateNumber, ComfortLevel
                            FROM Vehicle
                            WHERE VehicleID = @VehicleID";

                        using (SqlCommand command = new SqlCommand(vehicleQuery, connection))
                        {
                            command.Parameters.AddWithValue("@VehicleID", route.VehicleID.Value);

                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    vehicle = new Vehicle(
                                        (int)reader["VehicleID"],
                                        (int)reader["UID"],
                                        reader["Brand"]?.ToString() ?? "",
                                        reader["Model"]?.ToString() ?? "",
                                        reader["Color"]?.ToString() ?? "",
                                        reader["PlateNumber"]?.ToString() ?? "",
                                        reader["ComfortLevel"]?.ToString() ?? ""
                                    );
                                }
                            }
                        }
                    }

                    ViewBag.Route = route;
                    ViewBag.Driver = driver;
                    ViewBag.Vehicle = vehicle;

                    // Get driver's review statistics if driver exists
                    if (driver != null)
                    {
                        string reviewQuery = @"
                            SELECT 
                                AVG(CAST(Rating AS FLOAT)) AS AverageRating,
                                COUNT(*) AS ReviewCount
                            FROM Review 
                            WHERE ReviewedUserID = @DriverID";

                        using (SqlCommand command = new SqlCommand(reviewQuery, connection))
                        {
                            command.Parameters.AddWithValue("@DriverID", driver.UID);

                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    var averageRating = reader["AverageRating"] != DBNull.Value ? (double)reader["AverageRating"] : 0.0;
                                    var reviewCount = reader["ReviewCount"] != DBNull.Value ? (int)reader["ReviewCount"] : 0;
                                    
                                    ViewBag.DriverAverageRating = averageRating;
                                    ViewBag.DriverReviewCount = reviewCount;
                                }
                                else
                                {
                                    ViewBag.DriverAverageRating = 0.0;
                                    ViewBag.DriverReviewCount = 0;
                                }
                            }
                        }
                    }
                    else
                    {
                        ViewBag.DriverAverageRating = 0.0;
                        ViewBag.DriverReviewCount = 0;
                    }

                    return View();
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while retrieving route information: " + ex.Message;
                return RedirectToAction("Index");
            }
        }
    }
}