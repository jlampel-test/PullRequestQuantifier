namespace PullRequestQuantifier.Repository.Service.Models
{
    using System.Globalization;
    using CsvHelper.Configuration;

    public sealed class CommitStatsTableEntityMap : ClassMap<CommitStatsTableEntity>
    {
        public CommitStatsTableEntityMap()
        {
            AutoMap(CultureInfo.InvariantCulture);
            Map(m => m.PartitionKey).Ignore();
            Map(m => m.RowKey).Ignore();
            Map(m => m.Timestamp).Ignore();
            Map(m => m.ETag).Ignore();
        }
    }
}
