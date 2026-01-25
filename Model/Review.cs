namespace SamkørselApp.Model
{
    public class Review
    {
        public int ReviewID { get; set; }
        public int RouteID { get; set; }
        public int ReviewerID { get; set; }
        public int ReviewedUserID { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; } = "";
        public DateTime Date { get; set; }
    }

    public class ReviewViewModel
    {
        public int BookingID { get; set; }
        public int RouteID { get; set; }
        public int ReviewedUserID { get; set; }
        public string ReviewedUserName { get; set; } = "";
        public string StartCity { get; set; } = "";
        public string EndCity { get; set; } = "";
        public DateTime Departure { get; set; }
        public int Rating { get; set; } = 5;
        public string Comment { get; set; } = "";
        public bool HasExistingReview { get; set; }
    }

    public class ReviewDisplayViewModel
    {
        public int ReviewID { get; set; }
        public int RouteID { get; set; }
        public string ReviewerName { get; set; } = "";
        public string ReviewedUserName { get; set; } = "";
        public int Rating { get; set; }
        public string Comment { get; set; } = "";
        public DateTime Date { get; set; }
        public string StartCity { get; set; } = "";
        public string EndCity { get; set; } = "";
    }
}