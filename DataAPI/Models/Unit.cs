namespace DataAPI.Models
{
    /// <summary>
    /// Единица измерения товара (шт., уп., фл., мл и т.д.).
    /// </summary>
    public class Unit
    {
        public const string DefaultId = "unit-pcs";

        public string Id { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
    }
}
