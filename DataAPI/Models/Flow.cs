namespace DataAPI.Models
{
    public class Flow
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string BranchId { get; set; } = Branch.DefaultId;

        public int QuantityIncome { get; set; }
        public int QuantityOutcome { get; set; }
        public int ReserveIncome { get; set; }
        public int ReserveOutcome { get; set; }
    }
}
