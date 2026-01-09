namespace SamkørselApp.Model
{
    public class Route
    {
        public int RouteID { get; set; }
        public int UID { get; set; }
        public int StartCityID { get; set; }
        public City? StartCity { get; set; }  // Navigation property
        public int EndCityID { get; set; }
        public City? EndCity { get; set; }    // Navigation property
        public DateTime Departure { get; set; }
        public DateTime? Arrival { get; set; }
        public int AvailableSeats { get; set; }
        public decimal PricePerSeat { get; set; }
        public int? VehicleID { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; }
        public List<Itinerary> Itineraries { get; set; }

        public Route(int routeID, int uid, int startCityID, City startCity, int endCityID, City endCity, DateTime departure, DateTime? arrival, int availableSeats, decimal pricePerSeat, int? vehicleID, string? description, string status, List<Itinerary> itineraries)
        {
            RouteID = routeID;
            UID = uid;
            StartCityID = startCityID;
            StartCity = startCity;
            EndCityID = endCityID;
            EndCity = endCity;
            Departure = departure;
            Arrival = arrival;
            AvailableSeats = availableSeats;
            PricePerSeat = pricePerSeat;
            VehicleID = vehicleID;
            Description = description;
            Status = status;
            Itineraries = itineraries;
        }

    }
}