namespace DataAPI.Models
{
    public class Product
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int DeliveryDays { get; set; }
        public int StorageLife { get; set; }
    }
}
