namespace DataAPI.Common
{
    public static class CacheKeys
    {
        public const string Products = "products";
        public const string Flows = "flows";
        public const string Stocks = "stocks";
        public const string Recommendations = "recommendations";
        public const string Branches = "branches";
        public const string Units = "units";

        public static string All(string entity) => $"{entity}:all";

        public static string Count(string entity) => $"{entity}:count";

        public static string Page(string entity, int page, int pageSize) =>
            $"{entity}:page:{page}:{pageSize}";

        public static string Paged(
            string entity,
            int page,
            int pageSize,
            string? filter,
            string? sortBy,
            bool sortDescending) =>
             $"{Page(entity, page, pageSize)}" +
                    $"|filter={filter ?? string.Empty}" +
                    $"|sortBy={sortBy ?? string.Empty}" +
                    $"|sortDescending={sortDescending}";

        public static string ById(string entity, string id) =>
            $"{entity}:id:{id}";

        public static string IndexKey(string entity) =>
            $"{entity}:__keys";
    }
}
