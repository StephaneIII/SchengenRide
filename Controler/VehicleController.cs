using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;
using System.Security.Cryptography;
using System.Text;
using SamkørselApp.Model;

namespace SamkørselApp.Controllers
{
    public class VehicleController : Controller
    {
        private readonly string connectionString;

        public VehicleController()
        {
            ConnectionStringGetter connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        [HttpGet]
        public IActionResult Index()
        {
            // Check if user is logged in
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserName")))
            {
                return RedirectToAction("Index", "LogIn");
            }

            // get user's vehicles
            var vehicles = new List<Vehicle>();
            string? uid = HttpContext.Session.GetString("UID");

            if (string.IsNullOrEmpty(uid))
            {
                // User not logged in - redirect to login or return error
                return RedirectToAction("Login", "Account");
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string vehicleQuery = "SELECT VehicleID, Brand, Model, Color, PlateNumber, ComfortLevel FROM Vehicle WHERE UID = @UID";
                using SqlCommand command = new SqlCommand(vehicleQuery, connection);
                command.Parameters.AddWithValue("@UID", uid);

                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    vehicles.Add(new Vehicle(
                        (int)reader["VehicleID"],
                        int.Parse(uid),  // Now safe - we've verified uid isn't null
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

        [HttpPost]
        public IActionResult Index(string Brand, string Model, string Color, string PlateNumber, string ComfortLevel, string Password)
        {
            string hashedPassword = HashPassword(Password);
            string Username = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(Username))
            {
                return RedirectToAction("Index", "LogIn");
            }
            string UID = HttpContext.Session.GetString("UID");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT TOP (1) UID, UserName, Email FROM [ucollect].[dbo].[User] WHERE UserName = @Username AND Password = @Password";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Username", Username);
                command.Parameters.AddWithValue("@Password", hashedPassword);

                using SqlDataReader reader = command.ExecuteReader();
                // Password check - should show error when user is NOT found
                if (!reader.Read())
                {
                    ViewBag.Error = "Invalid password";
                    return View();
                }
            }




            if (string.IsNullOrEmpty(UID) || string.IsNullOrEmpty(Brand) || string.IsNullOrEmpty(Model) || string.IsNullOrEmpty(Color) || string.IsNullOrEmpty(PlateNumber) || string.IsNullOrEmpty(ComfortLevel))
            {
                ViewBag.Error = "All fields are required";
                return View();
            }


            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "INSERT INTO Vehicle (UID, Brand, Model, Color, PlateNumber, ComfortLevel) VALUES(@UID, @Brand, @Model, @Color, @PlateNumber, @ComfortLevel)";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UID", UID);
                command.Parameters.AddWithValue("@Brand", Brand);
                command.Parameters.AddWithValue("@Model", Model);
                command.Parameters.AddWithValue("@Color", Color);
                command.Parameters.AddWithValue("@PlateNumber", PlateNumber);
                command.Parameters.AddWithValue("@ComfortLevel", ComfortLevel);

                int rowsAffected = command.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    return RedirectToAction("Index", "Home");
                }
            }

            ViewBag.Error = "Invalid username or password";
            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }
    }
}