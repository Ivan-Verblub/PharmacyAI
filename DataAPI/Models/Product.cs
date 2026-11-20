namespace DataAPI.Models
{
    public class Product
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int DeliveryDays { get; set; }
        public int StorageLife { get; set; }

        /// <summary>
        /// Базовая единица измерения. В ней хранятся движения, остатки и рекомендации.
        /// </summary>
        public string UnitId { get; set; } = Unit.DefaultId;

        /// <summary>
        /// Единица закупки (например, упаковка). Если не задана — закупка в базовой единице.
        /// </summary>
        public string? PurchaseUnitId { get; set; }

        /// <summary>
        /// Количество базовых единиц в одной единице закупки. Итог рекомендации
        /// округляется вверх до кратного этому значению.
        /// </summary>
        public int PurchaseUnitRatio { get; set; } = 1;

        /// <summary>
        /// Минимальное количество (в базовых единицах), которое закупается безусловно
        /// в каждом филиале — даже при нулевом спросе или достаточном остатке.
        /// </summary>
        public int MinPurchaseQuantity { get; set; }
    }
}
