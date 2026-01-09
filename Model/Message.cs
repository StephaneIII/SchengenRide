namespace SamkørselApp.Model
{
    public class Message
    {
        public int MessageID { get; set; }
        public int ConversationID { get; set; }
        public int SenderID { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string MessageContent { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public class ConversationDetailsViewModel
    {
        public int ConversationID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ParticipantName { get; set; } = string.Empty;
        public int RouteID { get; set; }
        public List<Message> Messages { get; set; } = new List<Message>();
    }
}