using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;
using SamkørselApp.Model;
using System.Linq;

namespace SamkørselApp.Controllers
{
    public class BookingController : Controller
    {
        private readonly string connectionString;

        public BookingController()
        {
            ConnectionStringGetter connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        // Show available routes to book
        public IActionResult Index(RouteSearchCriteria? criteria = null)
        {
            // Load cities for search dropdowns
            ViewBag.Cities = GetCities();
            ViewBag.SearchCriteria = criteria ?? new RouteSearchCriteria();

            List<RouteViewModel> availableRoutes = GetFilteredRoutes(criteria);
            return View(availableRoutes);
        }

        [HttpPost]
        public IActionResult SearchRoutes(RouteSearchCriteria criteria)
        {
            return RedirectToAction("Index", criteria);
        }

        private List<RouteViewModel> GetFilteredRoutes(RouteSearchCriteria? criteria)
        {
            List<RouteViewModel> availableRoutes = new List<RouteViewModel>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                
                // Build dynamic query based on search criteria
                string query = @"
                    SELECT r.RouteID, r.UID, u.UserName AS DriverName, 
                           sc.CityName AS StartCity, ec.CityName AS EndCity, 
                           r.Departure, r.Arrival, r.AvailableSeats, r.PricePerSeat, 
                           r.Description, r.Status, 
                           ISNULL(v.Brand + ' ' + v.Model, 'Not specified') AS VehicleName,
                           v.ComfortLevel,
                           ISNULL(SUM(CAST(b.SeatsBooked AS INT)), 0) AS BookedSeats
                    FROM Route r
                    INNER JOIN [User] u ON r.UID = u.UID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    LEFT JOIN Vehicle v ON r.VehicleID = v.VehicleID
                    LEFT JOIN Booking b ON r.RouteID = b.RouteID AND b.Status = 'Confirmed'
                    WHERE r.Status = 'Active'";

                List<SqlParameter> parameters = new List<SqlParameter>();

                // Add search criteria filters
                if (criteria != null)
                {
                    if (!string.IsNullOrEmpty(criteria.DepartureCity))
                    {
                        query += " AND sc.CityName LIKE @DepartureCity";
                        parameters.Add(new SqlParameter("@DepartureCity", "%" + criteria.DepartureCity + "%"));
                    }

                    if (!string.IsNullOrEmpty(criteria.DestinationCity))
                    {
                        query += " AND ec.CityName LIKE @DestinationCity";
                        parameters.Add(new SqlParameter("@DestinationCity", "%" + criteria.DestinationCity + "%"));
                    }

                    if (criteria.MinPrice.HasValue)
                    {
                        query += " AND r.PricePerSeat >= @MinPrice";
                        parameters.Add(new SqlParameter("@MinPrice", criteria.MinPrice.Value));
                    }

                    if (criteria.MaxPrice.HasValue)
                    {
                        query += " AND r.PricePerSeat <= @MaxPrice";
                        parameters.Add(new SqlParameter("@MaxPrice", criteria.MaxPrice.Value));
                    }

                    if (!string.IsNullOrEmpty(criteria.MinComfortLevel))
                    {
                        // Map comfort level string to integer for database query
                        int? comfortLevelInt = MapComfortLevelToInt(criteria.MinComfortLevel);
                        if (comfortLevelInt.HasValue)
                        {
                            query += " AND (v.ComfortLevel >= @MinComfortLevel OR v.ComfortLevel IS NULL)";
                            parameters.Add(new SqlParameter("@MinComfortLevel", comfortLevelInt.Value));
                        }
                    }
                }

                query += @" GROUP BY r.RouteID, r.UID, u.UserName, sc.CityName, ec.CityName, 
                            r.Departure, r.Arrival, r.AvailableSeats, r.PricePerSeat, 
                            r.Description, r.Status, v.Brand, v.Model, v.ComfortLevel
                            HAVING r.AvailableSeats - ISNULL(SUM(b.SeatsBooked), 0) > 0";

                // Filter by minimum available seats if specified
                if (criteria?.MinAvailableSeats > 0)
                {
                    query += " AND r.AvailableSeats - ISNULL(SUM(b.SeatsBooked), 0) >= @MinAvailableSeats";
                    parameters.Add(new SqlParameter("@MinAvailableSeats", criteria.MinAvailableSeats));
                }

                query += " ORDER BY r.Departure";

                using SqlCommand command = new SqlCommand(query, connection);
                foreach (var param in parameters)
                {
                    command.Parameters.Add(param);
                }

                using SqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    int routeId = (int)reader["RouteID"];
                    int availableSeats = (int)reader["AvailableSeats"];
                    int bookedSeats = (int)reader["BookedSeats"];
                    
                    // Map comfort level integer to string
                    int? comfortLevelInt = reader["ComfortLevel"] == DBNull.Value ? (int?)null : (int)reader["ComfortLevel"];
                    string comfortLevel = MapComfortLevel(comfortLevelInt);

                    var routeViewModel = new RouteViewModel
                    {
                        RouteID = routeId,
                        DriverName = reader["DriverName"]?.ToString() ?? "",
                        StartCity = reader["StartCity"]?.ToString() ?? "",
                        EndCity = reader["EndCity"]?.ToString() ?? "",
                        Departure = (DateTime)reader["Departure"],
                        Arrival = reader["Arrival"] == DBNull.Value ? (DateTime?)null : (DateTime)reader["Arrival"],
                        TotalSeats = availableSeats,
                        RemainingSeats = availableSeats - bookedSeats,
                        PricePerSeat = (decimal)reader["PricePerSeat"],
                        Description = reader["Description"]?.ToString(),
                        VehicleName = reader["VehicleName"]?.ToString() ?? "Not specified",
                        ComfortLevel = comfortLevel
                    };

                    availableRoutes.Add(routeViewModel);
                }
            }

            // Load route stops for search results
            foreach (var route in availableRoutes)
            {
                route.RouteStops = GetRouteStops(route.RouteID);
            }

            return availableRoutes;
        }

        private List<City> GetCities()
        {
            List<City> cities = new List<City>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT CityID, CityName, CityXCoord, CityYCoord FROM City ORDER BY CityName";

                using SqlCommand command = new SqlCommand(query, connection);
                using SqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    cities.Add(new City(
                        (int)reader["CityID"],
                        reader["CityName"].ToString(),
                        (double)reader["CityXCoord"],
                        (double)reader["CityYCoord"]
                    ));
                }
            }

            return cities;
        }

        private List<string> GetRouteStops(int routeId)
        {
            List<string> stops = new List<string>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT c.CityName
                    FROM Itinerary i
                    INNER JOIN City c ON i.CityID = c.CityID
                    WHERE i.RouteID = @RouteID
                    ORDER BY i.StopOrder";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@RouteID", routeId);
                using SqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    stops.Add(reader["CityName"].ToString());
                }
            }

            return stops;
        }

        // Helper methods to map comfort levels between int and string
        private string MapComfortLevel(int? comfortLevelInt)
        {
            return comfortLevelInt switch
            {
                1 => "Basic",
                2 => "Standard",
                3 => "Premium",
                4 => "Luxury",
                _ => "Standard" // Default
            };
        }

        private int? MapComfortLevelToInt(string comfortLevel)
        {
            return comfortLevel switch
            {
                "Basic" => 1,
                "Standard" => 2,
                "Premium" => 3,
                "Luxury" => 4,
                _ => null
            };
        }

        // Show booking form for a specific route
        public IActionResult Book(int id)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            RouteViewModel? route = GetRouteWithAvailability(id);
            
            if (route == null)
            {
                TempData["Error"] = "Route not found.";
                return RedirectToAction("Index");
            }

            if (route.RemainingSeats <= 0)
            {
                TempData["Error"] = "No seats available on this route.";
                return RedirectToAction("Index");
            }

            return View(route);
        }

        // Process booking
        [HttpPost]
        public IActionResult Book(int routeId, int seatsToBook)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            // Get route with current availability
            RouteViewModel? route = GetRouteWithAvailability(routeId);

            if (route == null)
            {
                TempData["Error"] = "Route not found.";
                return RedirectToAction("Index");
            }

            if (seatsToBook <= 0)
            {
                TempData["Error"] = "Please select at least 1 seat.";
                return RedirectToAction("Book", new { id = routeId });
            }

            if (seatsToBook > route.RemainingSeats)
            {
                TempData["Error"] = $"Only {route.RemainingSeats} seats available.";
                return RedirectToAction("Book", new { id = routeId });
            }

            // Calculate total price
            decimal totalPrice = route.PricePerSeat * seatsToBook;

            // Create booking
            int driverId = 0;
            string driverName = "";
            string routeTitle = "";
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // First get the driver info and route details for the conversation
                string getRouteInfoQuery = @"
                    SELECT r.UID as DriverID, u.UserName as DriverName, 
                           sc.CityName as StartCity, ec.CityName as EndCity
                    FROM Route r
                    INNER JOIN [User] u ON r.UID = u.UID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    WHERE r.RouteID = @RouteID";

                using SqlCommand routeInfoCommand = new SqlCommand(getRouteInfoQuery, connection);
                routeInfoCommand.Parameters.AddWithValue("@RouteID", routeId);
                
                using SqlDataReader reader = routeInfoCommand.ExecuteReader();
                if (reader.Read())
                {
                    driverId = (int)reader["DriverID"];
                    driverName = reader["DriverName"].ToString() ?? "";
                    routeTitle = $"Samkørsel: {reader["StartCity"]} → {reader["EndCity"]}";
                }
                reader.Close();

                // Create the booking
                string insertQuery = @"
                    INSERT INTO Booking (RouteID, PassengerID, Status, SeatsBooked, PricePaid)
                    VALUES (@RouteID, @PassengerID, @Status, @SeatsBooked, @PricePaid)";

                using SqlCommand command = new SqlCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@RouteID", routeId);
                command.Parameters.AddWithValue("@PassengerID", int.Parse(uid));
                command.Parameters.AddWithValue("@Status", "Pending");
                command.Parameters.AddWithValue("@SeatsBooked", seatsToBook);
                command.Parameters.AddWithValue("@PricePaid", totalPrice);

                command.ExecuteNonQuery();
            }

            // Create conversation between passenger and driver if it doesn't exist
            if (driverId > 0 && driverId != int.Parse(uid)) // Don't create conversation with yourself
            {
                try
                {
                    MyWebApp.Controllers.ChatController chatController = new MyWebApp.Controllers.ChatController();
                    chatController.CreateConversationIfNotExists(
                        int.Parse(uid), // passenger
                        driverId,       // driver
                        routeId,        // route
                        routeTitle      // conversation title
                    );
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the booking
                    System.Diagnostics.Debug.WriteLine($"Failed to create conversation: {ex.Message}");
                }
            }

            TempData["Success"] = $"Booking request submitted! {seatsToBook} seat(s) requested for €{totalPrice:F2}. Waiting for driver approval.";
            return RedirectToAction("MyBookings");
        }

        // View user's bookings
        public IActionResult MyBookings()
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            List<BookingViewModel> bookings = new List<BookingViewModel>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        b.BookingID,
                        b.BookingDate,
                        b.Status,
                        b.SeatsBooked,
                        b.PricePaid,
                        sc.CityName AS StartCity,
                        ec.CityName AS EndCity,
                        r.Departure,
                        u.UserName AS DriverName
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    INNER JOIN [User] u ON r.UID = u.UID
                    WHERE b.PassengerID = @PassengerID
                    ORDER BY b.BookingDate DESC";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@PassengerID", int.Parse(uid));

                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    bookings.Add(new BookingViewModel
                    {
                        BookingID = (int)reader["BookingID"],
                        BookingDate = (DateTime)reader["BookingDate"],
                        Status = reader["Status"]?.ToString() ?? "",
                        SeatsBooked = (int)reader["SeatsBooked"],
                        PricePaid = (decimal)reader["PricePaid"],
                        StartCity = reader["StartCity"]?.ToString() ?? "",
                        EndCity = reader["EndCity"]?.ToString() ?? "",
                        Departure = (DateTime)reader["Departure"],
                        DriverName = reader["DriverName"]?.ToString() ?? ""
                    });
                }
            }

            return View(bookings);
        }

        // Helper method to get route with availability
        private RouteViewModel? GetRouteWithAvailability(int routeId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        r.RouteID,
                        u.UserName AS DriverName,
                        sc.CityName AS StartCity,
                        ec.CityName AS EndCity,
                        r.Departure,
                        r.Arrival,
                        r.AvailableSeats,
                        r.PricePerSeat,
                        r.Description,
                        CASE 
                            WHEN v.Brand IS NOT NULL AND v.Model IS NOT NULL 
                            THEN v.Brand + ' ' + v.Model 
                            ELSE 'Not specified' 
                        END AS VehicleName,
                        v.ComfortLevel
                    FROM Route r
                    INNER JOIN [User] u ON r.UID = u.UID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    LEFT JOIN Vehicle v ON r.VehicleID = v.VehicleID
                    WHERE r.RouteID = @RouteID";

                RouteViewModel? routeViewModel = null;
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@RouteID", routeId);

                    using SqlDataReader reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        // Map comfort level integer to string
                        int? comfortLevelInt = reader["ComfortLevel"] == DBNull.Value ? (int?)null : (int)reader["ComfortLevel"];
                        string comfortLevel = MapComfortLevel(comfortLevelInt);

                        routeViewModel = new RouteViewModel
                        {
                            RouteID = (int)reader["RouteID"],
                            DriverName = reader["DriverName"]?.ToString() ?? "",
                            StartCity = reader["StartCity"]?.ToString() ?? "",
                            EndCity = reader["EndCity"]?.ToString() ?? "",
                            Departure = (DateTime)reader["Departure"],
                            Arrival = reader["Arrival"] == DBNull.Value ? (DateTime?)null : (DateTime)reader["Arrival"],
                            TotalSeats = (int)reader["AvailableSeats"],
                            PricePerSeat = (decimal)reader["PricePerSeat"],
                            Description = reader["Description"]?.ToString(),
                            VehicleName = reader["VehicleName"]?.ToString() ?? "Not specified",
                            ComfortLevel = comfortLevel
                        };
                    }
                }

                if (routeViewModel != null)
                {
                    // Get booked seats in a separate query
                    string bookedSeatsQuery = @"
                        SELECT ISNULL(SUM(CAST(SeatsBooked AS INT)), 0) AS BookedSeats 
                        FROM Booking 
                        WHERE RouteID = @RouteID AND Status = 'Confirmed'";
                    
                    using SqlCommand bookedCommand = new SqlCommand(bookedSeatsQuery, connection);
                    bookedCommand.Parameters.AddWithValue("@RouteID", routeId);
                    
                    int bookedSeats = Convert.ToInt32(bookedCommand.ExecuteScalar() ?? 0);
                    routeViewModel.RemainingSeats = routeViewModel.TotalSeats - bookedSeats;
                }

                return routeViewModel;
            }
        }

        // View booking requests for driver's routes
        public IActionResult BookingRequests()
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            List<BookingRequestViewModel> requests = new List<BookingRequestViewModel>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        b.BookingID,
                        b.BookingDate,
                        b.Status,
                        b.SeatsBooked,
                        b.PricePaid,
                        sc.CityName AS StartCity,
                        ec.CityName AS EndCity,
                        r.Departure,
                        r.RouteID,
                        p.UserName AS PassengerName,
                        p.Email AS PassengerEmail
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    INNER JOIN [User] p ON b.PassengerID = p.UID
                    WHERE r.UID = @DriverID
                    ORDER BY 
                        CASE WHEN b.Status = 'Pending' THEN 0 ELSE 1 END,
                        b.BookingDate DESC";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@DriverID", int.Parse(uid));

                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    requests.Add(new BookingRequestViewModel
                    {
                        BookingID = (int)reader["BookingID"],
                        BookingDate = (DateTime)reader["BookingDate"],
                        Status = reader["Status"]?.ToString() ?? "",
                        SeatsBooked = (int)reader["SeatsBooked"],
                        PricePaid = (decimal)reader["PricePaid"],
                        StartCity = reader["StartCity"]?.ToString() ?? "",
                        EndCity = reader["EndCity"]?.ToString() ?? "",
                        Departure = (DateTime)reader["Departure"],
                        RouteID = (int)reader["RouteID"],
                        PassengerName = reader["PassengerName"]?.ToString() ?? "",
                        PassengerEmail = reader["PassengerEmail"]?.ToString() ?? ""
                    });
                }
            }

            return View(requests);
        }

        // Approve a pending booking
        [HttpPost]
        public IActionResult ApproveBooking(int bookingId)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Verify this booking belongs to the driver's route and check seat availability
                string verifyQuery = @"
                    SELECT b.SeatsBooked, r.AvailableSeats, 
                           ISNULL((SELECT SUM(b2.SeatsBooked) FROM Booking b2 
                                   WHERE b2.RouteID = r.RouteID AND b2.Status = 'Confirmed'), 0) AS ConfirmedSeats
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    WHERE b.BookingID = @BookingID AND r.UID = @DriverID AND b.Status = 'Pending'";

                using SqlCommand verifyCommand = new SqlCommand(verifyQuery, connection);
                verifyCommand.Parameters.AddWithValue("@BookingID", bookingId);
                verifyCommand.Parameters.AddWithValue("@DriverID", int.Parse(uid));

                using SqlDataReader reader = verifyCommand.ExecuteReader();
                if (!reader.Read())
                {
                    TempData["Error"] = "Booking not found or you don't have permission to approve it.";
                    return RedirectToAction("BookingRequests");
                }

                int seatsToBook = (int)reader["SeatsBooked"];
                int availableSeats = (int)reader["AvailableSeats"];
                int confirmedSeats = (int)reader["ConfirmedSeats"];
                int remainingSeats = availableSeats - confirmedSeats;

                reader.Close();

                if (seatsToBook > remainingSeats)
                {
                    TempData["Error"] = $"Cannot approve: Only {remainingSeats} seats remaining but {seatsToBook} requested.";
                    return RedirectToAction("BookingRequests");
                }

                // Update booking status to Confirmed
                string updateQuery = "UPDATE Booking SET Status = 'Confirmed' WHERE BookingID = @BookingID";
                using SqlCommand updateCommand = new SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@BookingID", bookingId);
                updateCommand.ExecuteNonQuery();
            }

            TempData["Success"] = "Booking approved successfully!";
            return RedirectToAction("BookingRequests");
        }

        // Reject a pending booking
        [HttpPost]
        public IActionResult RejectBooking(int bookingId)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Verify this booking belongs to the driver's route
                string verifyQuery = @"
                    SELECT b.BookingID
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    WHERE b.BookingID = @BookingID AND r.UID = @DriverID AND b.Status = 'Pending'";

                using SqlCommand verifyCommand = new SqlCommand(verifyQuery, connection);
                verifyCommand.Parameters.AddWithValue("@BookingID", bookingId);
                verifyCommand.Parameters.AddWithValue("@DriverID", int.Parse(uid));

                object? result = verifyCommand.ExecuteScalar();
                if (result == null)
                {
                    TempData["Error"] = "Booking not found or you don't have permission to reject it.";
                    return RedirectToAction("BookingRequests");
                }

                // Update booking status to Rejected
                string updateQuery = "UPDATE Booking SET Status = 'Rejected' WHERE BookingID = @BookingID";
                using SqlCommand updateCommand = new SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@BookingID", bookingId);
                updateCommand.ExecuteNonQuery();
            }

            TempData["Success"] = "Booking rejected.";
            return RedirectToAction("BookingRequests");
        }

        // Cancel a booking by the passenger
        [HttpPost]
        public IActionResult CancelBooking(int bookingId, string cancellationReason)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            if (string.IsNullOrWhiteSpace(cancellationReason))
            {
                TempData["Error"] = "Please provide a reason for cancellation.";
                return RedirectToAction("MyBookings");
            }

            int routeId = 0;
            int driverId = 0;
            string routeTitle = "";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Get booking details and verify ownership
                string verifyQuery = @"
                    SELECT b.Status, r.RouteID, r.UID as DriverID, 
                           sc.CityName as StartCity, ec.CityName as EndCity
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    WHERE b.BookingID = @BookingID AND b.PassengerID = @PassengerID";

                using SqlCommand verifyCommand = new SqlCommand(verifyQuery, connection);
                verifyCommand.Parameters.AddWithValue("@BookingID", bookingId);
                verifyCommand.Parameters.AddWithValue("@PassengerID", int.Parse(uid));

                using SqlDataReader reader = verifyCommand.ExecuteReader();
                if (!reader.Read())
                {
                    TempData["Error"] = "Booking not found or you don't have permission to cancel it.";
                    return RedirectToAction("MyBookings");
                }

                string currentStatus = reader["Status"]?.ToString() ?? "";
                if (currentStatus == "Cancelled")
                {
                    TempData["Error"] = "Booking is already cancelled.";
                    return RedirectToAction("MyBookings");
                }

                routeId = (int)reader["RouteID"];
                driverId = (int)reader["DriverID"];
                routeTitle = $"Samkørsel: {reader["StartCity"]} → {reader["EndCity"]}";
                reader.Close();

                // Update booking status to Cancelled
                string updateQuery = "UPDATE Booking SET Status = 'Cancelled' WHERE BookingID = @BookingID";
                using SqlCommand updateCommand = new SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@BookingID", bookingId);
                updateCommand.ExecuteNonQuery();
            }

            // Send cancellation message to driver via chat
            if (driverId > 0 && driverId != int.Parse(uid))
            {
                try
                {
                    MyWebApp.Controllers.ChatController chatController = new MyWebApp.Controllers.ChatController();
                    int conversationId = chatController.CreateConversationIfNotExists(
                        int.Parse(uid), // passenger
                        driverId,       // driver
                        routeId,        // route
                        routeTitle      // conversation title
                    );

                    // Send the cancellation message
                    string cancellationMessage = $"🚫 Booking aflyst / Booking Cancelled\n\nBegrundelse / Reason: {cancellationReason.Trim()}";
                    
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        connection.Open();
                        string insertMessageQuery = @"
                            INSERT INTO Message (ConversationID, SenderID, MessageContent)
                            VALUES (@ConversationID, @SenderID, @MessageContent)";

                        using SqlCommand command = new SqlCommand(insertMessageQuery, connection);
                        command.Parameters.AddWithValue("@ConversationID", conversationId);
                        command.Parameters.AddWithValue("@SenderID", int.Parse(uid));
                        command.Parameters.AddWithValue("@MessageContent", cancellationMessage);
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the cancellation
                    System.Diagnostics.Debug.WriteLine($"Failed to send cancellation message: {ex.Message}");
                }
            }

            TempData["Success"] = "Booking cancelled successfully. A message with your reason has been sent to the driver.";
            return RedirectToAction("MyBookings");
        }
    }

    // ViewModel for displaying routes
    public class RouteViewModel
    {
        public int RouteID { get; set; }
        public string DriverName { get; set; } = "";
        public string StartCity { get; set; } = "";
        public string EndCity { get; set; } = "";
        public DateTime Departure { get; set; }
        public DateTime? Arrival { get; set; }
        public int TotalSeats { get; set; }
        public int RemainingSeats { get; set; }
        public decimal PricePerSeat { get; set; }
        public string? Description { get; set; }
        public string VehicleName { get; set; } = "";
        public string ComfortLevel { get; set; } = "Standard";
        public List<string> RouteStops { get; set; } = new List<string>();
    }

    // ViewModel for search criteria
    public class RouteSearchCriteria
    {
        public string? DepartureCity { get; set; }
        public string? DestinationCity { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public string? MinComfortLevel { get; set; }
        public int? MinAvailableSeats { get; set; }
        public string? RouteStops { get; set; } // For searching in route stops
    }

    // ViewModel for displaying bookings
    public class BookingViewModel
    {
        public int BookingID { get; set; }
        public DateTime BookingDate { get; set; }
        public string Status { get; set; } = "";
        public int SeatsBooked { get; set; }
        public decimal PricePaid { get; set; }
        public string StartCity { get; set; } = "";
        public string EndCity { get; set; } = "";
        public DateTime Departure { get; set; }
        public string DriverName { get; set; } = "";
    }

    // ViewModel for driver's booking requests
    public class BookingRequestViewModel
    {
        public int BookingID { get; set; }
        public DateTime BookingDate { get; set; }
        public string Status { get; set; } = "";
        public int SeatsBooked { get; set; }
        public decimal PricePaid { get; set; }
        public string StartCity { get; set; } = "";
        public string EndCity { get; set; } = "";
        public DateTime Departure { get; set; }
        public int RouteID { get; set; }
        public string PassengerName { get; set; } = "";
        public string PassengerEmail { get; set; } = "";
    }
}