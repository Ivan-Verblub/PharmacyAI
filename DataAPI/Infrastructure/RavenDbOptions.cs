namespace DataAPI.Infrastructure
{
    public class RavenDbOptions
    {
        public const string SectionName = "RavenDb";

        public string[] Urls { get; set; } = [];
        public string Database { get; set; } = string.Empty;
        public int AggressiveCacheDurationSeconds { get; set; } = 60;
    }
}
