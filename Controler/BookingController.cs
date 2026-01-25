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
                           r.Description, r.Status, r.DistanceKm, r.ExpectedTravelTimeMinutes,
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

                    // Timespan filters
                    if (criteria.EarliestDeparture.HasValue)
                    {
                        query += " AND r.Departure >= @EarliestDeparture";
                        parameters.Add(new SqlParameter("@EarliestDeparture", criteria.EarliestDeparture.Value));
                    }

                    if (criteria.LatestDeparture.HasValue)
                    {
                        query += " AND r.Departure <= @LatestDeparture";
                        parameters.Add(new SqlParameter("@LatestDeparture", criteria.LatestDeparture.Value));
                    }

                    // Time of day filters
                    if (criteria.EarliestDepartureTime.HasValue)
                    {
                        query += " AND CAST(r.Departure AS TIME) >= @EarliestDepartureTime";
                        parameters.Add(new SqlParameter("@EarliestDepartureTime", criteria.EarliestDepartureTime.Value));
                    }

                    if (criteria.LatestDepartureTime.HasValue)
                    {
                        query += " AND CAST(r.Departure AS TIME) <= @LatestDepartureTime";
                        parameters.Add(new SqlParameter("@LatestDepartureTime", criteria.LatestDepartureTime.Value));
                    }
                }

                query += @" GROUP BY r.RouteID, r.UID, u.UserName, sc.CityName, ec.CityName, 
                            r.Departure, r.Arrival, r.AvailableSeats, r.PricePerSeat, 
                            r.Description, r.Status, r.DistanceKm, r.ExpectedTravelTimeMinutes, v.Brand, v.Model, v.ComfortLevel
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
                        ComfortLevel = comfortLevel,
                        DistanceKm = reader["DistanceKm"] == DBNull.Value ? (decimal?)null : (decimal)reader["DistanceKm"],
                        ExpectedTravelTimeMinutes = reader["ExpectedTravelTimeMinutes"] == DBNull.Value ? (int?)null : (int)reader["ExpectedTravelTimeMinutes"]
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
            int newBookingId = 0;
            
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
                    OUTPUT INSERTED.BookingID
                    VALUES (@RouteID, @PassengerID, @Status, @SeatsBooked, @PricePaid)";

                using SqlCommand command = new SqlCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@RouteID", routeId);
                command.Parameters.AddWithValue("@PassengerID", int.Parse(uid));
                command.Parameters.AddWithValue("@Status", "Pending");
                command.Parameters.AddWithValue("@SeatsBooked", seatsToBook);
                command.Parameters.AddWithValue("@PricePaid", totalPrice);

                newBookingId = (int)command.ExecuteScalar();
            }

            // Create conversation between passenger and driver if it doesn't exist and send automatic booking message
            if (driverId > 0 && driverId != int.Parse(uid)) // Don't create conversation with yourself
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

                    // Send automatic booking confirmation message with booking ID
                    string bookingMessage = $"🎯 New booking created!\n\n" +
                                          $"Booking ID: #{newBookingId}\n" +
                                          $"Seats requested: {seatsToBook}\n" +
                                          $"Total price: €{totalPrice:F2}\n" +
                                          $"Status: Pending driver approval\n\n" +
                                          $"Driver will be notified to approve your booking. You can use this Booking ID for any questions about your trip.";

                    SendAutomaticMessage(conversationId, int.Parse(uid), bookingMessage);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the booking
                    System.Diagnostics.Debug.WriteLine($"Failed to create conversation or send message: {ex.Message}");
                }
            }

            TempData["Success"] = $"Booking request submitted! {seatsToBook} seat(s) requested for €{totalPrice:F2}. Waiting for driver approval.";
            return RedirectToAction("MyBookings");
        }

        // Helper method to send automatic messages
        private void SendAutomaticMessage(int conversationId, int senderId, string message)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string insertMessageQuery = @"
                    INSERT INTO Message (ConversationID, SenderID, MessageContent)
                    VALUES (@ConversationID, @SenderID, @MessageContent)";

                using SqlCommand command = new SqlCommand(insertMessageQuery, connection);
                command.Parameters.AddWithValue("@ConversationID", conversationId);
                command.Parameters.AddWithValue("@SenderID", senderId);
                command.Parameters.AddWithValue("@MessageContent", message);

                command.ExecuteNonQuery();
            }
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
                        r.RouteID,
                        r.UID AS DriverID,
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
                        RouteID = (int)reader["RouteID"],
                        DriverID = (int)reader["DriverID"],
                        DriverName = reader["DriverName"]?.ToString() ?? ""
                    });
                }
            }

            return View(bookings);
        }

        // Show review form for a completed booking
        public IActionResult Review(int bookingId)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            ReviewViewModel? reviewModel = GetReviewViewModel(bookingId, int.Parse(uid));
            
            if (reviewModel == null)
            {
                TempData["Error"] = "Booking not found or not eligible for review.";
                return RedirectToAction("MyBookings");
            }

            return View(reviewModel);
        }

        // Process review submission
        [HttpPost]
        public IActionResult Review(ReviewViewModel model)
        {
            string? uid = HttpContext.Session.GetString("UID");
            if (string.IsNullOrEmpty(uid))
            {
                return RedirectToAction("Index", "LogIn");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Validate rating
            if (model.Rating < 1 || model.Rating > 5)
            {
                ModelState.AddModelError("Rating", "Rating must be between 1 and 5 stars.");
                return View(model);
            }

            // Check if review already exists
            if (HasExistingReview(model.RouteID, int.Parse(uid), model.ReviewedUserID))
            {
                TempData["Error"] = "You have already reviewed this trip.";
                return RedirectToAction("MyBookings");
            }

            // Insert review
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string insertQuery = @"
                    INSERT INTO Review (RouteID, ReviewerID, ReviewedUserID, Rating, Comment)
                    VALUES (@RouteID, @ReviewerID, @ReviewedUserID, @Rating, @Comment)";

                using SqlCommand command = new SqlCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@RouteID", model.RouteID);
                command.Parameters.AddWithValue("@ReviewerID", int.Parse(uid));
                command.Parameters.AddWithValue("@ReviewedUserID", model.ReviewedUserID);
                command.Parameters.AddWithValue("@Rating", model.Rating);
                command.Parameters.AddWithValue("@Comment", model.Comment ?? "");

                command.ExecuteNonQuery();
            }

            TempData["Success"] = "Thank you for your review! Your feedback has been submitted.";
            return RedirectToAction("MyBookings");
        }

        // Helper method to get review view model
        private ReviewViewModel? GetReviewViewModel(int bookingId, int userId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        b.BookingID,
                        b.RouteID,
                        r.Departure,
                        r.UID AS DriverID,
                        u.UserName AS DriverName,
                        sc.CityName AS StartCity,
                        ec.CityName AS EndCity
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    INNER JOIN [User] u ON r.UID = u.UID
                    INNER JOIN City sc ON r.StartCityID = sc.CityID
                    INNER JOIN City ec ON r.EndCityID = ec.CityID
                    WHERE b.BookingID = @BookingID 
                    AND b.PassengerID = @PassengerID 
                    AND b.Status = 'Confirmed'
                    AND r.Departure < GETDATE()";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@BookingID", bookingId);
                command.Parameters.AddWithValue("@PassengerID", userId);

                using SqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var model = new ReviewViewModel
                    {
                        BookingID = (int)reader["BookingID"],
                        RouteID = (int)reader["RouteID"],
                        ReviewedUserID = (int)reader["DriverID"],
                        ReviewedUserName = reader["DriverName"]?.ToString() ?? "",
                        StartCity = reader["StartCity"]?.ToString() ?? "",
                        EndCity = reader["EndCity"]?.ToString() ?? "",
                        Departure = (DateTime)reader["Departure"]
                    };

                    reader.Close();

                    // Check if review already exists
                    model.HasExistingReview = HasExistingReview(model.RouteID, userId, model.ReviewedUserID);
                    
                    return model;
                }
            }

            return null;
        }

        // Helper method to check if review already exists
        private bool HasExistingReview(int routeId, int reviewerId, int reviewedUserId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT COUNT(*) 
                    FROM Review 
                    WHERE RouteID = @RouteID 
                    AND ReviewerID = @ReviewerID 
                    AND ReviewedUserID = @ReviewedUserID";

                using SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@RouteID", routeId);
                command.Parameters.AddWithValue("@ReviewerID", reviewerId);
                command.Parameters.AddWithValue("@ReviewedUserID", reviewedUserId);

                return (int)command.ExecuteScalar() > 0;
            }
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
                        r.DistanceKm,
                        r.ExpectedTravelTimeMinutes,
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
                            ComfortLevel = comfortLevel,
                            DistanceKm = reader["DistanceKm"] == DBNull.Value ? (decimal?)null : (decimal)reader["DistanceKm"],
                            ExpectedTravelTimeMinutes = reader["ExpectedTravelTimeMinutes"] == DBNull.Value ? (int?)null : (int)reader["ExpectedTravelTimeMinutes"]
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

                // Get booking details for the automatic message
                string getBookingDetailsQuery = @"
                    SELECT b.PassengerID, b.SeatsBooked, b.PricePaid, r.RouteID
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    WHERE b.BookingID = @BookingID";

                int passengerId = 0;
                int seatsBooked = 0;
                decimal pricePaid = 0;
                int routeId = 0;

                using SqlCommand detailsCommand = new SqlCommand(getBookingDetailsQuery, connection);
                detailsCommand.Parameters.AddWithValue("@BookingID", bookingId);
                using SqlDataReader detailsReader = detailsCommand.ExecuteReader();
                if (detailsReader.Read())
                {
                    passengerId = (int)detailsReader["PassengerID"];
                    seatsBooked = (int)detailsReader["SeatsBooked"];
                    pricePaid = (decimal)detailsReader["PricePaid"];
                    routeId = (int)detailsReader["RouteID"];
                }
                detailsReader.Close();

                // Send automatic approval message
                if (passengerId > 0 && routeId > 0)
                {
                    try
                    {
                        // Find the conversation between driver and passenger
                        string findConversationQuery = @"
                            SELECT c.ConversationID
                            FROM Conversation c
                            INNER JOIN ConversationParticipant cp1 ON c.ConversationID = cp1.ConversationID
                            INNER JOIN ConversationParticipant cp2 ON c.ConversationID = cp2.ConversationID
                            WHERE c.RouteID = @RouteID
                            AND cp1.UserID = @DriverID
                            AND cp2.UserID = @PassengerID
                            AND cp1.UserID != cp2.UserID";

                        using SqlCommand convCommand = new SqlCommand(findConversationQuery, connection);
                        convCommand.Parameters.AddWithValue("@RouteID", routeId);
                        convCommand.Parameters.AddWithValue("@DriverID", int.Parse(uid));
                        convCommand.Parameters.AddWithValue("@PassengerID", passengerId);

                        object conversationResult = convCommand.ExecuteScalar();
                        if (conversationResult != null)
                        {
                            int conversationId = (int)conversationResult;
                            string approvalMessage = $"✅ Booking APPROVED!\n\n" +
                                                    $"Booking ID: #{bookingId}\n" +
                                                    $"Seats confirmed: {seatsBooked}\n" +
                                                    $"Total price: €{pricePaid:F2}\n" +
                                                    $"Status: Confirmed\n\n" +
                                                    $"Your booking has been approved by the driver. Have a great trip! 🚗✨";

                            SendAutomaticMessage(conversationId, int.Parse(uid), approvalMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to send approval message: {ex.Message}");
                    }
                }
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

                // Get booking details for the automatic message before updating
                string getBookingDetailsQuery = @"
                    SELECT b.PassengerID, b.SeatsBooked, b.PricePaid, r.RouteID
                    FROM Booking b
                    INNER JOIN Route r ON b.RouteID = r.RouteID
                    WHERE b.BookingID = @BookingID";

                int passengerId = 0;
                int seatsBooked = 0;
                decimal pricePaid = 0;
                int routeId = 0;

                using SqlCommand detailsCommand = new SqlCommand(getBookingDetailsQuery, connection);
                detailsCommand.Parameters.AddWithValue("@BookingID", bookingId);
                using SqlDataReader detailsReader = detailsCommand.ExecuteReader();
                if (detailsReader.Read())
                {
                    passengerId = (int)detailsReader["PassengerID"];
                    seatsBooked = (int)detailsReader["SeatsBooked"];
                    pricePaid = (decimal)detailsReader["PricePaid"];
                    routeId = (int)detailsReader["RouteID"];
                }
                detailsReader.Close();

                // Update booking status to Rejected
                string updateQuery = "UPDATE Booking SET Status = 'Rejected' WHERE BookingID = @BookingID";
                using SqlCommand updateCommand = new SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@BookingID", bookingId);
                updateCommand.ExecuteNonQuery();

                // Send automatic rejection message
                if (passengerId > 0 && routeId > 0)
                {
                    try
                    {
                        // Find the conversation between driver and passenger
                        string findConversationQuery = @"
                            SELECT c.ConversationID
                            FROM Conversation c
                            INNER JOIN ConversationParticipant cp1 ON c.ConversationID = cp1.ConversationID
                            INNER JOIN ConversationParticipant cp2 ON c.ConversationID = cp2.ConversationID
                            WHERE c.RouteID = @RouteID
                            AND cp1.UserID = @DriverID
                            AND cp2.UserID = @PassengerID
                            AND cp1.UserID != cp2.UserID";

                        using SqlCommand convCommand = new SqlCommand(findConversationQuery, connection);
                        convCommand.Parameters.AddWithValue("@RouteID", routeId);
                        convCommand.Parameters.AddWithValue("@DriverID", int.Parse(uid));
                        convCommand.Parameters.AddWithValue("@PassengerID", passengerId);

                        object conversationResult = convCommand.ExecuteScalar();
                        if (conversationResult != null)
                        {
                            int conversationId = (int)conversationResult;
                            string rejectionMessage = $"❌ Booking REJECTED\n\n" +
                                                     $"Booking ID: #{bookingId}\n" +
                                                     $"Seats requested: {seatsBooked}\n" +
                                                     $"Amount: €{pricePaid:F2}\n" +
                                                     $"Status: Rejected\n\n" +
                                                     $"Unfortunately, your booking request has been declined by the driver. Please try booking another route.";

                            SendAutomaticMessage(conversationId, int.Parse(uid), rejectionMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to send rejection message: {ex.Message}");
                    }
                }
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
        public decimal? DistanceKm { get; set; }
        public int? ExpectedTravelTimeMinutes { get; set; }
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
        
        // Timespan filters
        public DateTime? EarliestDeparture { get; set; }
        public DateTime? LatestDeparture { get; set; }
        public TimeSpan? EarliestDepartureTime { get; set; } // Time of day filter
        public TimeSpan? LatestDepartureTime { get; set; } // Time of day filter
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
        public int RouteID { get; set; }
        public int DriverID { get; set; }
        public bool IsPastBooking => Departure < DateTime.Now;
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