namespace MyWebApp.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Data.SqlClient;
    using SamkørselApp.Helper;
    using SamkørselApp.Model;

    public class ChatController : Controller
    {
        private readonly string connectionString;

        public ChatController()
        {
            ConnectionStringGetter connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        // GET: /Chat/
        public IActionResult Index()
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            var conversations = GetAllConversations(int.Parse(uid));
            return View(conversations);
        }

        // Get list of all ConversationSummary for the current user
        public List<ConversationSummary> GetAllConversations(int userId)
        {
            List<ConversationSummary> conversations = new List<ConversationSummary>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        c.ConversationID,
                        c.Title,
                        c.RouteID,
                        (
                            SELECT TOP 1 u.UserName 
                            FROM ConversationParticipant cp2 
                            INNER JOIN [User] u ON cp2.UserID = u.UID 
                            WHERE cp2.ConversationID = c.ConversationID AND cp2.UserID != @UserID
                        ) AS ParticipantName,
                        (
                            SELECT TOP 1 m.MessageContent 
                            FROM Message m 
                            WHERE m.ConversationID = c.ConversationID 
                            ORDER BY m.SentAt DESC
                        ) AS LastMessage,
                        (
                            SELECT TOP 1 m.SentAt 
                            FROM Message m 
                            WHERE m.ConversationID = c.ConversationID 
                            ORDER BY m.SentAt DESC
                        ) AS LastUpdated
                    FROM Conversation c
                    INNER JOIN ConversationParticipant cp ON c.ConversationID = cp.ConversationID
                    WHERE cp.UserID = @UserID
                    ORDER BY LastUpdated DESC";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserID", userId);

                using SqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    conversations.Add(new ConversationSummary(
                        conversationId: (int)reader["ConversationID"],
                        participantName: reader["ParticipantName"]?.ToString() ?? "Unknown",
                        title: reader["Title"]?.ToString() ?? "",
                        lastMessage: reader["LastMessage"]?.ToString() ?? "",
                        lastUpdated: reader["LastUpdated"] != DBNull.Value ? (DateTime)reader["LastUpdated"] : DateTime.MinValue,
                        routeID: reader["RouteID"] != DBNull.Value ? (int)reader["RouteID"] : 0
                    ));
                }
            }

            return conversations;
        }

        // Get unread message count for the notification badge
        public int GetUnreadMessageCount(int userId)
        {
            int count = 0;

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

            return count;
        }

        // Create conversation between two users if it doesn't exist
        public int CreateConversationIfNotExists(int userId1, int userId2, int routeId, string title)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Check if conversation already exists between these users for this route
                string checkQuery = @"
                    SELECT TOP 1 c.ConversationID 
                    FROM Conversation c
                    INNER JOIN ConversationParticipant cp1 ON c.ConversationID = cp1.ConversationID
                    INNER JOIN ConversationParticipant cp2 ON c.ConversationID = cp2.ConversationID
                    WHERE c.RouteID = @RouteID 
                    AND cp1.UserID = @UserId1 
                    AND cp2.UserID = @UserId2
                    AND cp1.UserID != cp2.UserID";

                using SqlCommand checkCommand = new SqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@RouteID", routeId);
                checkCommand.Parameters.AddWithValue("@UserId1", userId1);
                checkCommand.Parameters.AddWithValue("@UserId2", userId2);

                var existingConversationId = checkCommand.ExecuteScalar();
                
                if (existingConversationId != null)
                {
                    // Conversation already exists
                    return (int)existingConversationId;
                }

                // Create new conversation
                string insertConversationQuery = @"
                    INSERT INTO Conversation (Title, RouteID, CreatedBy)
                    OUTPUT INSERTED.ConversationID
                    VALUES (@Title, @RouteID, @CreatedBy)";

                using SqlCommand insertCommand = new SqlCommand(insertConversationQuery, connection);
                insertCommand.Parameters.AddWithValue("@Title", title);
                insertCommand.Parameters.AddWithValue("@RouteID", routeId);
                insertCommand.Parameters.AddWithValue("@CreatedBy", userId1);

                int newConversationId = (int)insertCommand.ExecuteScalar();

                // Add both users as participants
                string addParticipantQuery = @"
                    INSERT INTO ConversationParticipant (ConversationID, UserID, IsAdmin)
                    VALUES (@ConversationID, @UserID, @IsAdmin)";

                // Add first user (creator) as admin
                using SqlCommand addUser1Command = new SqlCommand(addParticipantQuery, connection);
                addUser1Command.Parameters.AddWithValue("@ConversationID", newConversationId);
                addUser1Command.Parameters.AddWithValue("@UserID", userId1);
                addUser1Command.Parameters.AddWithValue("@IsAdmin", true);
                addUser1Command.ExecuteNonQuery();

                // Add second user as regular participant
                using SqlCommand addUser2Command = new SqlCommand(addParticipantQuery, connection);
                addUser2Command.Parameters.AddWithValue("@ConversationID", newConversationId);
                addUser2Command.Parameters.AddWithValue("@UserID", userId2);
                addUser2Command.Parameters.AddWithValue("@IsAdmin", false);
                addUser2Command.ExecuteNonQuery();

                // Send initial automated message
                string initialMessage = "Hej! En samtale er blevet oprettet automatisk for jeres samkørsel. God tur! 🚗";
                
                string insertMessageQuery = @"
                    INSERT INTO Message (ConversationID, SenderID, MessageContent)
                    VALUES (@ConversationID, @SenderID, @MessageContent)";

                using SqlCommand messageCommand = new SqlCommand(insertMessageQuery, connection);
                messageCommand.Parameters.AddWithValue("@ConversationID", newConversationId);
                messageCommand.Parameters.AddWithValue("@SenderID", userId1); // System message from creator
                messageCommand.Parameters.AddWithValue("@MessageContent", initialMessage);
                messageCommand.ExecuteNonQuery();

                return newConversationId;
            }
        }

        // GET: /Chat/Conversation/5
        public IActionResult Conversation(int id)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            var conversationDetails = GetConversationDetails(id, int.Parse(uid));
            
            if (conversationDetails == null)
            {
                TempData["Error"] = "Conversation not found or you don't have access to it.";
                return RedirectToAction("Index");
            }
            // Update user's last seen time for this conversation
            UpdateLastSeenTime(id, int.Parse(uid));
            return View(conversationDetails);
        }

        // POST: Send a message in the conversation
        [HttpPost]
        public IActionResult SendMessage(int conversationId, string message)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                TempData["Error"] = "Message cannot be empty.";
                return RedirectToAction("Conversation", new { id = conversationId });
            }

            // Verify user has access to this conversation
            if (!HasAccessToConversation(conversationId, int.Parse(uid)))
            {
                TempData["Error"] = "You don't have access to this conversation.";
                return RedirectToAction("Index");
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string insertMessageQuery = @"
                    INSERT INTO Message (ConversationID, SenderID, MessageContent)
                    VALUES (@ConversationID, @SenderID, @MessageContent)";

                using SqlCommand command = new SqlCommand(insertMessageQuery, connection);
                command.Parameters.AddWithValue("@ConversationID", conversationId);
                command.Parameters.AddWithValue("@SenderID", int.Parse(uid));
                command.Parameters.AddWithValue("@MessageContent", message.Trim());

                command.ExecuteNonQuery();
            }

            return RedirectToAction("Conversation", new { id = conversationId });
        }

        // Get conversation details with all messages
        private ConversationDetailsViewModel? GetConversationDetails(int conversationId, int currentUserId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // First verify the user has access to this conversation
                if (!HasAccessToConversation(conversationId, currentUserId))
                {
                    return null;
                }

                // Get conversation details
                string conversationQuery = @"
                    SELECT 
                        c.ConversationID,
                        c.Title,
                        c.RouteID,
                        (
                            SELECT TOP 1 u.UserName 
                            FROM ConversationParticipant cp2 
                            INNER JOIN [User] u ON cp2.UserID = u.UID 
                            WHERE cp2.ConversationID = c.ConversationID AND cp2.UserID != @CurrentUserId
                        ) AS ParticipantName
                    FROM Conversation c
                    WHERE c.ConversationID = @ConversationID";

                ConversationDetailsViewModel? conversationDetails = null;

                using SqlCommand conversationCommand = new SqlCommand(conversationQuery, connection);
                conversationCommand.Parameters.AddWithValue("@ConversationID", conversationId);
                conversationCommand.Parameters.AddWithValue("@CurrentUserId", currentUserId);

                using SqlDataReader conversationReader = conversationCommand.ExecuteReader();
                if (conversationReader.Read())
                {
                    conversationDetails = new ConversationDetailsViewModel
                    {
                        ConversationID = (int)conversationReader["ConversationID"],
                        Title = conversationReader["Title"]?.ToString() ?? "",
                        RouteID = conversationReader["RouteID"] != DBNull.Value ? (int)conversationReader["RouteID"] : 0,
                        ParticipantName = conversationReader["ParticipantName"]?.ToString() ?? "Unknown"
                    };
                }
                conversationReader.Close();

                if (conversationDetails == null)
                {
                    return null;
                }

                // Get all messages for this conversation
                string messagesQuery = @"
                    SELECT 
                        m.MessageID,
                        m.ConversationID,
                        m.SenderID,
                        u.UserName AS SenderName,
                        m.MessageContent,
                        m.SentAt
                    FROM Message m
                    INNER JOIN [User] u ON m.SenderID = u.UID
                    WHERE m.ConversationID = @ConversationID
                    ORDER BY m.SentAt ASC";

                using SqlCommand messagesCommand = new SqlCommand(messagesQuery, connection);
                messagesCommand.Parameters.AddWithValue("@ConversationID", conversationId);

                using SqlDataReader messagesReader = messagesCommand.ExecuteReader();
                while (messagesReader.Read())
                {
                    conversationDetails.Messages.Add(new Message
                    {
                        MessageID = (int)messagesReader["MessageID"],
                        ConversationID = (int)messagesReader["ConversationID"],
                        SenderID = (int)messagesReader["SenderID"],
                        SenderName = messagesReader["SenderName"]?.ToString() ?? "",
                        MessageContent = messagesReader["MessageContent"]?.ToString() ?? "",
                        SentAt = (DateTime)messagesReader["SentAt"],
                        IsCurrentUser = (int)messagesReader["SenderID"] == currentUserId
                    });
                }

                return conversationDetails;
            }
        }

        // Check if user has access to the conversation
        private bool HasAccessToConversation(int conversationId, int userId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT COUNT(*)
                    FROM ConversationParticipant cp
                    WHERE cp.ConversationID = @ConversationID AND cp.UserID = @UserID";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@ConversationID", conversationId);
                command.Parameters.AddWithValue("@UserID", userId);

                int count = (int)command.ExecuteScalar();
                return count > 0;
            }
        }

        // Update the user's last seen time for a conversation
        private void UpdateLastSeenTime(int conversationId, int userId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Update or insert LastSeenAt - first try to update
                string updateQuery = @"
                    UPDATE ConversationParticipant 
                    SET LastSeenAt = GETDATE() 
                    WHERE ConversationID = @ConversationID AND UserID = @UserID";

                using SqlCommand command = new SqlCommand(updateQuery, connection);
                command.Parameters.AddWithValue("@ConversationID", conversationId);
                command.Parameters.AddWithValue("@UserID", userId);

                command.ExecuteNonQuery();
            }
        }
    }
}