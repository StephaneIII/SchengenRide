
namespace SamkørselApp.Model
{
    public class User
    {
        public int UID { get; set; }


        public string UserName { get; set; }


        public string Password { get; set; }


        public string Email { get; set; }

        public string? Phone { get; set; }

        public string? ProfilePictureURL { get; set; }

        public double? Rating { get; set; }

        public DateTime JoinDate { get; set; } = DateTime.Now;

        public User(string Username, string Password, string Email, string? Phone, string? ProfilePictureURL)
        {
            this.UserName = Username;
            this.Password = Password;
            this.Email = Email;
            this.Phone = Phone;
            this.ProfilePictureURL = ProfilePictureURL;
        }

        public User(int UID, string Username, string Password, string Email, string? Phone, string? ProfilePictureURL, double? Rating,  DateTime JoinDate)
        {
            this.UID = UID;
            this.UserName = Username;
            this.Password = Password;
            this.Email = Email;
            this.Phone = Phone;
            this.ProfilePictureURL = ProfilePictureURL;
            this.Rating = Rating;
            this.JoinDate = JoinDate;
        }
    }
}