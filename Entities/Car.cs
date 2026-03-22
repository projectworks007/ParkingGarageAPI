using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ParkingGarageAPI.Entities
{
    public class Car
    {
        [JsonIgnore]
        public int Id { get; set; }
        
        [Required]
        [JsonPropertyName("brand")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Brand { get; set; }
        
        [Required]
        [JsonPropertyName("model")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Model { get; set; }
        
        [Required]
        [JsonPropertyName("year")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Year { get; set; }

        [Required]
        [JsonPropertyName("licensePlate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [MaxLength(7)]
        public string LicensePlate { get; set; }

        // Kapcsolat a User entitással
        [JsonIgnore]
        public int UserId { get; set; }
        
        [JsonIgnore] // Megakadályozza a körkörös referenciát JSON szerializáláskor
        public User? User { get; set; }
        
        // Kapcsolat a ParkingSpot entitással
        [JsonIgnore]
        public ParkingSpot? ParkingSpot { get; set; }
        
        // Parkolás státusza - új autó regisztrációjakor alapértelmezetten nincs leparkolva
        [JsonIgnore]
        public bool IsParked { get; set; } = false;
    }
}
