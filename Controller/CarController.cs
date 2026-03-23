using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ParkingGarageAPI.Context;
using ParkingGarageAPI.Entities;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;

namespace ParkingGarageAPI.Controller;

[Route("api/cars")]
[ApiController]
[Authorize]
public class CarController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    
    public CarController(ApplicationDbContext context)
    {
        _context = context;
    }
    
    [HttpGet]
    public IActionResult GetUserCars()
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("User not authenticated.");
                
            var user = _context.Users
                .Include(u => u.Cars)
                .FirstOrDefault(u => u.Email == userEmail);
            
            if (user == null)
                return NotFound("User not found.");
                
            if (user.Cars == null)
            {
                user.Cars = new List<Car>();
            }
            
            // Átalakítjuk az adatokat, hogy minden mező (beleértve az ID-t is) látható legyen
            var formattedCars = user.Cars.Select(c => new
            {
                c.Id,
                c.Brand,
                c.Model,
                c.Year,
                c.LicensePlate,
                c.IsParked
            }).ToList();
                
            return Ok(formattedCars);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
    
    [HttpPost]
    public IActionResult AddCar([FromBody] Car car)
    {
        try
        {
            if (car == null)
                return BadRequest("Car data is null");
                
            if (string.IsNullOrWhiteSpace(car.LicensePlate) || car.LicensePlate.Length > 7)
                return BadRequest("License plate must be 1-7 characters long.");
                
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("User not authenticated.");
                
            var user = _context.Users.FirstOrDefault(u => u.Email == userEmail);
            
            if (user == null)
                return NotFound("User not found.");
            
            // Generálunk ID-t, ha nincs megadva
            if (car.Id <= 0)
            {
                int newId = _context.Cars.Any() ? _context.Cars.Max(c => c.Id) + 1 : 1;
                car.Id = newId;
            }
            
            // Biztosítjuk, hogy az IsParked értéke mindig false legyen új autó esetén
            car.IsParked = false;
                
            car.UserId = user.Id;
            _context.Cars.Add(car);
            _context.SaveChanges();
            
            // Visszadjuk az új autó adatait, beleértve az ID-t is
            return Ok(new
            {
                message = "Car added successfully.",
                car = new
                {
                    car.Id,
                    car.Brand,
                    car.Model,
                    car.Year,
                    car.LicensePlate,
                    car.UserId,
                    car.IsParked
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCar(int id)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized("User not authenticated.");
                
            var user = _context.Users.FirstOrDefault(u => u.Email == userEmail);
            
            if (user == null)
                return NotFound("User not found.");
            
            Car car;
            if (user.IsAdmin)
            {
                car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            }
            else
            {
                car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id);
            }
            
            if (car == null)
                return NotFound("Car not found or you don't have permission to delete it.");

            // Normál felhasználó ne tudjon aktív parkolás alatt törölni.
            if (!user.IsAdmin && car.IsParked)
                return BadRequest("Parkoló autó nem törölhető. Előbb állítsd le a parkolást.");

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                // Admin törléskor az aktív parkolás felszabadítása kötelező.
                if (user.IsAdmin && car.IsParked)
                {
                    var parkingSpot = await _context.ParkingSpots
                        .FirstOrDefaultAsync(p => p.CarId == car.Id);

                    if (parkingSpot == null)
                    {
                        throw new InvalidOperationException("Nem található aktív parkolóhely az autóhoz, ezért a törlés megszakadt.");
                    }

                    parkingSpot.IsOccupied = false;
                    parkingSpot.CarId = null;
                    parkingSpot.EndTime = DateTime.Now;
                    car.IsParked = false;
                }

                _context.Cars.Remove(car);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });
            
            return Ok("Car deleted successfully.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("all")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult GetAllCars()
    {
        try
        {
            var cars = _context.Cars.Include(c => c.User).ToList();
            
            // Eltávolítjuk a referencia hurkokat a JSON szerializáláskor
            var carsWithoutCycles = cars.Select(c => new
            {
                c.Id,
                c.Brand,
                c.Model,
                c.Year,
                c.LicensePlate,
                c.UserId,
                c.IsParked,
                UserName = c.User != null ? $"{c.User.FirstName} {c.User.LastName}" : "Unknown",
                UserEmail = c.User != null ? c.User.Email : "Unknown"
            }).ToList();
            
            return Ok(carsWithoutCycles);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Belső szerverhiba: {ex.Message}");
        }
    }
} 