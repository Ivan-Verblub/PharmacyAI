namespace DataAPI.Models
{
    public class Stock
    {
        public string Id { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string BranchId { get; set; } = Branch.DefaultId;

        public int Quantity { get; set; }
        public int Reserve { get; set; }
        public int Transit { get; set; }
    }
}
