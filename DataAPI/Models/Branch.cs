namespace DataAPI.Models
{
    /// <summary>
    /// Филиал (аптека, склад). Движения, остатки и рекомендации привязываются к филиалу.
    /// </summary>
    public class Branch
    {
        public const string DefaultId = "branch-main";

        public string Id { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
