namespace SamkørselApp.Model
{
    public class City
    {
        public int CityID { get; set; }

        public string CityName { get; set; }

        public double CityXCoord { get; set; }

        public double CityYCoord { get; set; }

        public City(int cityID, string cityName, double cityXCoord, double cityYCoord)
        {
            CityID = cityID;
            CityName = cityName;
            CityXCoord = cityXCoord;
            CityYCoord = cityYCoord;
        }

    }
}