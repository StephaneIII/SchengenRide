using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;

namespace MyWebApp.ViewComponents
{
    [ViewComponent(Name = "UnreadMessages")]
    public class UnreadMessagesViewComponent : ViewComponent
    {
        private readonly string connectionString;

        public UnreadMessagesViewComponent()
        {
            ConnectionStringGetter connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        public IViewComponentResult Invoke()
        {
            string? uid = HttpContext.Session.GetString("UID");
            string? isLoggedIn = HttpContext.Session.GetString("IsLoggedIn");
            
            if (string.IsNullOrEmpty(uid) || isLoggedIn != "true")
            {
                return View(0); 
            }

            int unreadCount = GetUnreadMessageCount(int.Parse(uid));
            return View(unreadCount);
        }

        private int GetUnreadMessageCount(int userId)
        {
            int count = 0;

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    // Count messages from other users that are newer than user's last seen time
                    string query = @"
                        SELECT COUNT(*) 
                        FROM Message m
                        INNER JOIN ConversationParticipant cp ON m.ConversationID = cp.ConversationID
                        WHERE cp.UserID = @UserID 
                        AND m.SenderID != @UserID
                        AND m.SentAt > ISNULL(cp.LastSeenAt, cp.JoinedAt)";

                    using SqlCommand command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@UserID", userId);

                    object result = command.ExecuteScalar();
                    count = result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch
            {
                // In case of any database errors, return 0
                count = 0;
            }

            return count;
        }
    }
}