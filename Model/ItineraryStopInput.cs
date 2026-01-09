namespace SamkørselApp.Model
{
    public class ItineraryStopInput
    {
        public int CityID { get; set; }
        public int StopOrder { get; set; }
        public DateTime? MinArrivalTime { get; set; }
        public double? MaximalDelay { get; set; }
    }
}
