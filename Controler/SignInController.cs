using SamkørselApp.Model;
using SamkørselApp.Helper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MailKit.Net.Smtp;
using MimeKit;
using System.Security.Cryptography;
using System.Text;

namespace SamkørselApp.Controllers
{
    public class SignInController : Controller
    { 
        private readonly string connectionString;
        private readonly IConfiguration _configuration;

        public SignInController(IConfiguration configuration)
        {
            _configuration = configuration;
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

        private string GenerateConfirmationCode()
        {
            Random random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        public async Task SendConfirmationEmail(string toEmail, string code)
        {
            var smtpServer = _configuration["EmailSettings:SmtpServer"];
            var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"]);
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var senderName = _configuration["EmailSettings:SenderName"];
            var appPassword = _configuration["EmailSettings:AppPassword"];

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "Your Confirmation Code";

            message.Body = new TextPart("plain")
            {
                Text = $"Your confirmation code is: {code}"
            };

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpServer, smtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(senderEmail, appPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(string Username, string Password, string Email, string Phone, string ProfilePictureURL)
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password) || string.IsNullOrEmpty(Email))
            {
                return View();
            }

            // Generate confirmation code
            string code = GenerateConfirmationCode();

            // Store user data in session
            HttpContext.Session.SetString("PendingUsername", Username);
            HttpContext.Session.SetString("PendingPassword", Password);
            HttpContext.Session.SetString("PendingEmail", Email);
            HttpContext.Session.SetString("PendingPhone", Phone ?? "");
            HttpContext.Session.SetString("PendingProfilePictureURL", ProfilePictureURL ?? "");
            HttpContext.Session.SetString("ConfirmationCode", code);

            // Send confirmation email
            await SendConfirmationEmail(Email, code);

            // Redirect to verification page
            return RedirectToAction("Verify");
        }

        [HttpGet]
        public IActionResult Verify()
        {
            // Check if there's pending registration
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("PendingEmail")))
            {
                return RedirectToAction("Index");
            }
            return View();
        }

        [HttpPost]
        public IActionResult Verify(string Code)
        {
            string storedCode = HttpContext.Session.GetString("ConfirmationCode");

            if (string.IsNullOrEmpty(Code) || Code != storedCode)
            {
                ViewBag.Error = "Invalid confirmation code. Please try again.";
                return View();
            }

            // Code is correct - create user in database
            string Username = HttpContext.Session.GetString("PendingUsername");
            string Password = HttpContext.Session.GetString("PendingPassword");
            string Email = HttpContext.Session.GetString("PendingEmail");
            string Phone = HttpContext.Session.GetString("PendingPhone");
            string ProfilePictureURL = HttpContext.Session.GetString("PendingProfilePictureURL");

            // Hash the password before storing
            string hashedPassword = HashPassword(Password);

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string SqlQuery = "INSERT INTO [ucollect].[dbo].[User] (UserName, Password, Email, Phone, ProfilePictureURL, JoinDate) VALUES (@UserName, @Password, @Email, @Phone, @ProfilePictureURL, GETDATE())";
                using (var command = new SqlCommand(SqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@UserName", Username);
                    command.Parameters.AddWithValue("@Password", hashedPassword);
                    command.Parameters.AddWithValue("@Email", Email);
                    command.Parameters.AddWithValue("@Phone", string.IsNullOrEmpty(Phone) ? DBNull.Value : Phone);
                    command.Parameters.AddWithValue("@ProfilePictureURL", string.IsNullOrEmpty(ProfilePictureURL) ? DBNull.Value : ProfilePictureURL);

                    int rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected == 1)
                    {
                        // Clear session data
                        HttpContext.Session.Remove("PendingUsername");
                        HttpContext.Session.Remove("PendingPassword");
                        HttpContext.Session.Remove("PendingEmail");
                        HttpContext.Session.Remove("PendingPhone");
                        HttpContext.Session.Remove("PendingProfilePictureURL");
                        HttpContext.Session.Remove("ConfirmationCode");

                        return RedirectToAction("Index", "Home");
                    }
                }
            }

            ViewBag.Error = "Failed to create user. Please try again.";
            return View();
        }
    }
}