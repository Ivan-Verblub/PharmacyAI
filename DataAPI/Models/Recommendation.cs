namespace DataAPI.Models
{
    public class Recommendation
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
    public class RecommendationAnalysisRequest
    {
        public DateTime TargetDate { get; set; }
    }

    public class RecommendationAnalysisStatus
    {
        public bool HasRecommendations { get; set; }
        public DateTime? LastRecommendationDate { get; set; }
        public int TotalCount { get; set; }
    }
}
