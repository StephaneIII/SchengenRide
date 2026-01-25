using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;
using SamkørselApp.Model;

namespace SamkørselApp.Controllers
{
    public class ProfileController : Controller
    {
        private readonly string connectionString;

        public ProfileController()
        {
            ConnectionStringGetter connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        [HttpGet]
        public IActionResult Index()
        {
            // Check if user is logged in
            if (HttpContext.Session.GetString("IsLoggedIn") != "true")
            {
                return RedirectToAction("Index", "LogIn");
            }

            string uid = HttpContext.Session.GetString("UID");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT UID, UserName, Email, Phone, ProfilePictureURL, Rating, JoinDate FROM [ucollect].[dbo].[User] WHERE UID = @UID";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UID", uid);

                using SqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    ViewBag.UID = reader["UID"];
                    ViewBag.UserName = reader["UserName"];
                    ViewBag.Email = reader["Email"];
                    ViewBag.Phone = reader["Phone"] != DBNull.Value ? reader["Phone"] : "";
                    ViewBag.ProfilePictureURL = reader["ProfilePictureURL"] != DBNull.Value ? reader["ProfilePictureURL"] : "";
                    ViewBag.Rating = reader["Rating"] != DBNull.Value ? reader["Rating"] : "No rating yet";
                    ViewBag.JoinDate = reader["JoinDate"];
                }
            }

            // Fetch review statistics (average rating and count)
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string reviewQuery = @"
                    SELECT 
                        AVG(CAST(Rating AS FLOAT)) AS AverageRating,
                        COUNT(*) AS ReviewCount
                    FROM Review 
                    WHERE ReviewedUserID = @UID";
                
                using SqlCommand command = new SqlCommand(reviewQuery, connection);
                command.Parameters.AddWithValue("@UID", uid);

                using SqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var averageRating = reader["AverageRating"] != DBNull.Value ? (double)reader["AverageRating"] : 0.0;
                    var reviewCount = reader["ReviewCount"] != DBNull.Value ? (int)reader["ReviewCount"] : 0;
                    
                    ViewBag.AverageRating = averageRating;
                    ViewBag.ReviewCount = reviewCount;
                }
                else
                {
                    ViewBag.AverageRating = 0.0;
                    ViewBag.ReviewCount = 0;
                }
            }

            // Fetch user's vehicles (max 2)
            var vehicles = new List<Vehicle>();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string vehicleQuery = "SELECT TOP 2 VehicleID, Brand, Model, Color, PlateNumber, ComfortLevel FROM [ucollect].[dbo].[Vehicle] WHERE UID = @UID";
                using SqlCommand command = new SqlCommand(vehicleQuery, connection);
                command.Parameters.AddWithValue("@UID", uid);

                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    vehicles.Add(new Vehicle(
                        (int)reader["VehicleID"],
                        int.Parse(uid),
                        reader["Brand"].ToString(),
                        reader["Model"].ToString(),
                        reader["Color"].ToString(),
                        reader["PlateNumber"].ToString(),
                        reader["ComfortLevel"].ToString()
                    ));
                }
            }
            ViewBag.Vehicles = vehicles;

            // Calculate CO2 savings
            var co2Savings = CalculateCO2Savings(uid);
            ViewBag.TotalCO2Saved = co2Savings.TotalCO2Saved;
            ViewBag.TotalTripsAsDriver = co2Savings.TotalTripsAsDriver;
            ViewBag.TotalTripsAsPassenger = co2Savings.TotalTripsAsPassenger;
            ViewBag.TotalDistanceKm = co2Savings.TotalDistanceKm;

            return View();
        }

        [HttpGet]
        public IActionResult Edit()
        {
            if (HttpContext.Session.GetString("IsLoggedIn") != "true")
            {
                return RedirectToAction("Index", "LogIn");
            }

            string uid = HttpContext.Session.GetString("UID");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT UserName, Email, Phone, ProfilePictureURL FROM [ucollect].[dbo].[User] WHERE UID = @UID";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UID", uid);

                using SqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    ViewBag.UserName = reader["UserName"];
                    ViewBag.Email = reader["Email"];
                    ViewBag.Phone = reader["Phone"] != DBNull.Value ? reader["Phone"] : "";
                    ViewBag.ProfilePictureURL = reader["ProfilePictureURL"] != DBNull.Value ? reader["ProfilePictureURL"] : "";
                }
            }

            return View();
        }

        [HttpPost]
        public IActionResult Edit(string Phone, string ProfilePictureURL)
        {
            if (HttpContext.Session.GetString("IsLoggedIn") != "true")
            {
                return RedirectToAction("Index", "LogIn");
            }

            string uid = HttpContext.Session.GetString("UID");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "UPDATE [ucollect].[dbo].[User] SET Phone = @Phone, ProfilePictureURL = @ProfilePictureURL WHERE UID = @UID";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UID", uid);
                command.Parameters.AddWithValue("@Phone", string.IsNullOrEmpty(Phone) ? DBNull.Value : Phone);
                command.Parameters.AddWithValue("@ProfilePictureURL", string.IsNullOrEmpty(ProfilePictureURL) ? DBNull.Value : ProfilePictureURL);

                command.ExecuteNonQuery();
            }

            return RedirectToAction("Index");
        }

        private CO2SavingsData CalculateCO2Savings(string uid)
        {
            var co2Data = new CO2SavingsData();
            const decimal CO2_PER_KM = 150; // 150g CO2 per km

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                
                // Calculate CO2 savings as a driver
                string driverQuery = @"
                    SELECT 
                        r.RouteID,
                        r.DistanceKm,
                        ISNULL((SELECT SUM(b.SeatsBooked) FROM Booking b 
                               WHERE b.RouteID = r.RouteID AND b.Status = 'Confirmed'), 0) as TotalPassengers
                    FROM Route r
                    WHERE r.UID = @UID AND r.Status = 'Completed' AND r.DistanceKm IS NOT NULL";
                
                using (SqlCommand command = new SqlCommand(driverQuery, connection))
                {
                    command.Parameters.AddWithValue("@UID", uid);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var distanceKm = reader["DistanceKm"] != DBNull.Value ? (decimal)reader["DistanceKm"] : 0;
                            var totalPassengers = (int)reader["TotalPassengers"];
                            
                            if (totalPassengers > 0)
                            {
                                // CO2 saved = distance * CO2_PER_KM * (passengers / (passengers + 1 driver))
                                var co2Saved = distanceKm * CO2_PER_KM * totalPassengers / (totalPassengers + 1);
                                co2Data.TotalCO2Saved += co2Saved;
                                co2Data.TotalDistanceKm += distanceKm;
                                co2Data.TotalTripsAsDriver++;
                            }
                        }
                    }
                }
                
                // Calculate CO2 savings as a passenger
                string passengerQuery = @"
                    SELECT 
                        r.DistanceKm,
                        b.SeatsBooked,
                        (SELECT SUM(b2.SeatsBooked) FROM Booking b2 
                         WHERE b2.RouteID = r.RouteID AND b2.Status = 'Confirmed') as TotalPassengers
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    WHERE b.PassengerID = @UID AND b.Status = 'Confirmed' 
                          AND r.Status = 'Completed' AND r.DistanceKm IS NOT NULL";
                
                using (SqlCommand command = new SqlCommand(passengerQuery, connection))
                {
                    command.Parameters.AddWithValue("@UID", uid);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var distanceKm = reader["DistanceKm"] != DBNull.Value ? (decimal)reader["DistanceKm"] : 0;
                            var seatsBooked = (int)reader["SeatsBooked"];
                            var totalPassengers = reader["TotalPassengers"] != DBNull.Value ? (int)reader["TotalPassengers"] : 0;
                            
                            if (totalPassengers > 0)
                            {
                                // CO2 saved per passenger = distance * CO2_PER_KM * (user's seats / (total passengers + 1 driver))
                                var co2Saved = distanceKm * CO2_PER_KM * seatsBooked / (totalPassengers + 1);
                                co2Data.TotalCO2Saved += co2Saved;
                                co2Data.TotalDistanceKm += distanceKm;
                                co2Data.TotalTripsAsPassenger++;
                            }
                        }
                    }
                }
            }

            return co2Data;
        }
    }

    public class CO2SavingsData
    {
        public decimal TotalCO2Saved { get; set; }
        public int TotalTripsAsDriver { get; set; }
        public int TotalTripsAsPassenger { get; set; }
        public decimal TotalDistanceKm { get; set; }
    }
}
