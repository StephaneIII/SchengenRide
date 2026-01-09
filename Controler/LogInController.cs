using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;
using System.Security.Cryptography;
using System.Text;

namespace SamkørselApp.Controllers
{
    public class LogInController : Controller
    {
        private readonly string connectionString;

        public LogInController()
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
            return View();
        }

        [HttpPost]
        public IActionResult Index(string Username, string Password)
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            {
                ViewBag.Error = "Username and password are required";
                return View();
            }

            string hashedPassword = HashPassword(Password);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT TOP (1) UID, UserName, Email FROM [ucollect].[dbo].[User] WHERE UserName = @Username AND Password = @Password";
                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Username", Username);
                command.Parameters.AddWithValue("@Password", hashedPassword);

                using SqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    HttpContext.Session.SetString("UID", reader["UID"].ToString());
                    HttpContext.Session.SetString("UserName", reader["UserName"].ToString());
                    HttpContext.Session.SetString("Email", reader["Email"].ToString());
                    HttpContext.Session.SetString("IsLoggedIn", "true");
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