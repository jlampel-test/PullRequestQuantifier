namespace PullRequestQuantifier.Tools.QuantifyRepositories
{
    using System;

    public class CommitStats
    {
        public string CommitSha1 { get; set; }

        public int QuantifiedLinesAdded { get; set; }

        public int QuantifiedLinesDeleted { get; set; }

        public float PercentileAddition { get; set; }

        public float PercentileDeletion { get; set; }

        public float DiffPercentile { get; set; }

        public string Label { get; set; }

        public int AbsoluteLinesAdded { get; set; }

        public int AbsoluteLinesDeleted { get; set; }

        public TimeSpan TimeToMerge { get; set; }

        public string AuthorEmail { get; set; }

        public string AuthorName { get; set; }
    }
}
