namespace SamkørselApp.Model
{
    public class ConversationSummary
    {
        public int ConversationId { get; set; }
        public string ParticipantName { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? LastMessage { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public int RouteID { get; set; }

        public ConversationSummary()
        {
        }

        public ConversationSummary(int conversationId, string participantName, string? title, string? lastMessage, DateTime lastUpdated, int routeID)
        {
            ConversationId = conversationId;
            ParticipantName = participantName;
            Title = title;
            LastMessage = lastMessage;
            LastUpdated = lastUpdated;
            RouteID = routeID;
        }
    }
}