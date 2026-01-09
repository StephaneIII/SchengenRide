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
    }
}
