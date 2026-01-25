namespace SamkørselApp.Model
{
    public class AutoTimingRequest
    {
        public List<int> CityIds { get; set; } = new List<int>();
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
    }
}