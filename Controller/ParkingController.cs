using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ParkingGarageAPI.Context;
using ParkingGarageAPI.Entities;
using ParkingGarageAPI.Services;
using System.Security.Claims;

namespace ParkingGarageAPI.Controller;

[Route("api/parking")]
[ApiController]
[Authorize]
public class ParkingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IInvoiceService _invoiceService;
    
    public ParkingController(ApplicationDbContext context, IInvoiceService invoiceService)
    {
        _context = context;
        _invoiceService = invoiceService;
    }
    
    // Szabad parkolóhelyek lekérdezése
    [HttpGet("spots/available")]
    public IActionResult GetAvailableSpots()
    {
        try
        {
            var availableSpots = _context.ParkingSpots
                .Where(p => !p.IsOccupied)
                .ToList();
                
            return Ok(availableSpots);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}");
        }
    }
    
    // Összes parkolóhely lekérdezése
    [HttpGet("spots")]
    public IActionResult GetAllSpots()
    {
        try
        {
            var spots = _context.ParkingSpots.ToList();
            return Ok(spots);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}");
        }
    }
    
    // Parkolás kezdése
    [HttpPost("start")]
    public IActionResult StartParking([FromBody] StartParkingRequest request)
    {
        try
        {
            if (request == null || request.CarId <= 0 || request.ParkingSpotId <= 0)
                return BadRequest("Érvénytelen kérés");
                
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            var user = _context.Users
                .Include(u => u.Cars)
                .FirstOrDefault(u => u.Email == userEmail);
                
            if (user == null)
                return NotFound("Felhasználó nem található");
                
            var car = _context.Cars.FirstOrDefault(c => c.Id == request.CarId && c.UserId == user.Id);
            if (car == null)
                return NotFound("Az autó nem található vagy nem a tiéd");
                
            var parkingSpot = _context.ParkingSpots.FirstOrDefault(p => p.Id == request.ParkingSpotId);
            if (parkingSpot == null)
                return NotFound("A parkolóhely nem található");
                
            if (parkingSpot.IsOccupied)
                return BadRequest("A parkolóhely már foglalt");
                
            if (car.IsParked)
                return BadRequest("Az autó már le van parkolva");
                
            // Parkolás kezdése
            parkingSpot.IsOccupied = true;
            parkingSpot.CarId = car.Id;
            parkingSpot.StartTime = DateTime.Now;
            parkingSpot.EndTime = null;
            
            car.IsParked = true;
            
            _context.SaveChanges();
            
            return Ok(new {
                message = "Parkolás elkezdve",
                startTime = parkingSpot.StartTime,
                floor = parkingSpot.FloorNumber,
                spot = parkingSpot.SpotNumber
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}");
        }
    }
    
    // Parkolás befejezése
    [HttpPost("end")]
    public async Task<IActionResult> EndParking([FromBody] EndParkingRequest request)
    {
        try
        {
            if (request == null || request.CarId <= 0)
                return BadRequest("Érvénytelen kérés");
                
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("Nem vagy bejelentkezve");
                
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);
                
            if (user == null)
                return NotFound("Felhasználó nem található");
            
            // Autó keresése: admin bármelyik autót leállíthatja, normál user csak a sajátját.
            var carQuery = _context.Cars
                .Include(c => c.User)
                .Where(c => c.Id == request.CarId);

            if (!user.IsAdmin)
            {
                carQuery = carQuery.Where(c => c.UserId == user.Id);
            }

            var car = await carQuery.FirstOrDefaultAsync();
                
            if (car == null)
                return NotFound($"Az autó (ID: {request.CarId}) nem található vagy nincs jogosultságod hozzá");
            
            // Ellenőrizzük a parkolási állapotot
            if (!car.IsParked)
                return BadRequest($"Az autó (ID: {request.CarId}) nincs leparkolva");
            
            // Parkolóhely keresése
            var parkingSpot = await _context.ParkingSpots
                .FirstOrDefaultAsync(p => p.CarId == car.Id);
            
            if (parkingSpot == null)
            {
                // Ha nincs parkolóhely, de az autó parkolva van, akkor javítsuk az inkonzisztenciát
                car.IsParked = false;
                await _context.SaveChangesAsync();
                return BadRequest("Inkonzisztens állapot: az autó parkolva van, de nincs parkolóhely hozzárendelve. Az autó parkolási állapotát visszaállítottuk.");
            }
            
            // Parkolás befejezése
            if (parkingSpot.StartTime == null)
            {
                parkingSpot.StartTime = DateTime.Now.AddHours(-1); // Alapértelmezett: 1 órás parkolás
            }
                
            parkingSpot.EndTime = DateTime.Now;
            parkingSpot.IsOccupied = false;
            
            // Időtartam számítása
            TimeSpan parkingDuration = parkingSpot.EndTime.Value - parkingSpot.StartTime.Value;
            
            // Percenkénti díjszámítás (600 Ft/óra = 10 Ft/perc)
            decimal minuteRate = 600m / 60m; // 10 Ft/perc
            decimal parkingFee = (decimal)parkingDuration.TotalMinutes * minuteRate;
            
            // Kerekítés a legközelebbi 10 forintra
            parkingFee = Math.Ceiling(parkingFee / 10) * 10;
            
            // Parkolási adatok mentése a történeti táblába
            var historyOwner = car.User ?? user;
            var history = new ParkingHistory
            {
                StartTime = parkingSpot.StartTime.Value,
                EndTime = parkingSpot.EndTime.Value,
                FloorNumber = parkingSpot.FloorNumber,
                SpotNumber = parkingSpot.SpotNumber,
                Fee = parkingFee,
                CarId = car.Id,
                CarBrand = car.Brand,
                CarModel = car.Model,
                LicensePlate = car.LicensePlate,
                UserId = historyOwner.Id,
                UserName = $"{historyOwner.FirstName} {historyOwner.LastName}",
                UserEmail = historyOwner.Email
            };
            
            // Mentés előtt frissítsük a kapcsolódó entitások állapotát
            car.IsParked = false;
            
            // Előbb mentsük a history objektumot
            _context.ParkingHistories.Add(history);
            await _context.SaveChangesAsync();
            
            // Ezután frissítsük a parkolóhelyet
            parkingSpot.CarId = null;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var innerException = ex.InnerException != null ? ex.InnerException.Message : "Nincs belső kivétel";
                return StatusCode(500, $"Parkolóhely frissítési hiba: {ex.Message}. Belső hiba: {innerException}");
            }
            
            try
            {
                // Számla generálása
                var invoice = await _invoiceService.CreateInvoiceAsync(history);
                
                // Email küldése (aszinkron módon a háttérben, nem várjuk meg a befejezését)
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await _invoiceService.SendInvoiceByEmailAsync(invoice);
                    }
                    catch (Exception emailEx)
                    {
                        Console.WriteLine($"Email küldési hiba: {emailEx.Message}");
                    }
                });
                
                return Ok(new {
                    message = "Parkolás befejezve",
                    startTime = parkingSpot.StartTime,
                    endTime = parkingSpot.EndTime,
                    duration = $"{parkingDuration.Hours} óra {parkingDuration.Minutes} perc",
                    fee = $"{parkingFee} Ft",
                    rate = "600 Ft/óra",
                    invoiceNumber = invoice.InvoiceNumber
                });
            }
            catch (Exception ex)
            {
                // A parkolás lezárása már sikeresen megtörtént és mentve lett.
                // Itt csak a számla/email folyamat hibázott, ezért ne dobjunk 500-at.
                var innerException = ex.InnerException != null ? ex.InnerException.Message : "Nincs belső kivétel";
                Console.WriteLine($"Számla generálási hiba, de parkolás lezárva: {ex.Message}. Belső hiba: {innerException}");

                return Ok(new
                {
                    message = "Parkolás befejezve, de a számla generálása sikertelen.",
                    startTime = parkingSpot.StartTime,
                    endTime = parkingSpot.EndTime,
                    duration = $"{parkingDuration.Hours} óra {parkingDuration.Minutes} perc",
                    fee = $"{parkingFee} Ft",
                    rate = "600 Ft/óra",
                    invoiceWarning = "A számla jelenleg nem elérhető."
                });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}. StackTrace: {ex.StackTrace}");
        }
    }
    
    // Az aktuális felhasználó parkoló autóinak lekérdezése
    [HttpGet("my")]
    public IActionResult GetMyParkedCars()
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            var user = _context.Users
                .Include(u => u.Cars)
                .FirstOrDefault(u => u.Email == userEmail);
                
            if (user == null)
                return NotFound("Felhasználó nem található");
                
            var parkedCars = _context.Cars
                .Where(c => c.UserId == user.Id && c.IsParked)
                .Join(_context.ParkingSpots,
                    car => car.Id,
                    spot => spot.CarId,
                    (car, spot) => new {
                        Car = car,
                        ParkingSpot = spot,
                        StartTime = spot.StartTime
                    })
                .ToList();
                
            return Ok(parkedCars);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}");
        }
    }
    
    // Parkolás állapotának lekérdezése
    [HttpGet("status/{carId}")]
    public async Task<IActionResult> GetParkingStatus(int carId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("Nem vagy bejelentkezve");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return NotFound("Felhasználó nem található");

            var car = await _context.Cars
                .FirstOrDefaultAsync(c => c.Id == carId && c.UserId == user.Id);

            if (car == null)
                return NotFound("Az autó nem található vagy nem a tiéd");

            var parkingSpot = await _context.ParkingSpots
                .FirstOrDefaultAsync(p => p.CarId == car.Id && p.IsOccupied);

            if (parkingSpot == null)
                return Ok(new { isParked = false, message = "Az autó nincs leparkolva." });

            return Ok(new
            {
                isParked = true,
                startTime = parkingSpot.StartTime,
                floor = parkingSpot.FloorNumber,
                spot = parkingSpot.SpotNumber
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}");
        }
    }
}

// Request osztályok
public class StartParkingRequest
{
    public int CarId { get; set; }
    public int ParkingSpotId { get; set; }
}

public class EndParkingRequest
{
    public int CarId { get; set; }
} 