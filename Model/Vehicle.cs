namespace SamkørselApp.Model
{
    public class Vehicle
    {
        public int VehicleID { get; set; }

        public int UID { get; set; }

        public string Brand { get; set; }

        public string Model { get; set; }

        public string Color { get; set; }

        public string PlateNumber { get; set; }

        public string ComfortLevel { get; set; }

        public Vehicle(int VehicleID, int UID, string Brand, string Model, string Color, string PlateNumber, string ComfortLevel)
        {
            this.VehicleID = VehicleID;
            this.UID = UID;
            this.Brand = Brand;
            this.Model = Model;
            this.Color = Color;
            this.PlateNumber = PlateNumber;
            this.ComfortLevel = ComfortLevel;
        }
    }
}