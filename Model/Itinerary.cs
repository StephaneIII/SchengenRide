namespace SamkørselApp.Model
{
    public class Itinerary
    {
        public int RouteID { get; set; }
        public City City { get; set; }
        public int StopOrder { get; set; }
        public DateTime? MinArrivalTime { get; set; }
        public double? MaximalDelay { get; set; }

        public Itinerary(int routeID, City city, int stopOrder, DateTime? minArrivalTime, double? maximalDelay)
        {
            RouteID = routeID;
            City = city;
            StopOrder = stopOrder;
            MinArrivalTime = minArrivalTime;
            MaximalDelay = maximalDelay;
        }

    }
}