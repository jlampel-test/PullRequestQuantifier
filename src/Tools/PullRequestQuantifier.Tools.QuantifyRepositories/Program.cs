namespace PullRequestQuantifier.Tools.QuantifyRepositories
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using LibGit2Sharp;
    using PullRequestQuantifier.Abstractions.Core;
    using PullRequestQuantifier.Client.QuantifyClient;
    using PullRequestQuantifier.GitEngine.Extensions;
    using PullRequestQuantifier.Tools.Common;
    using PullRequestQuantifier.Tools.Common.Model;
    using YamlDotNet.Serialization;

    /// <summary>
    /// params : -clonepath {} -configfile {} -user {} -pat {} or only if you don; want to clone -RepoPath {}.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var commandLine = new CommandLine(args);

            var organizations = commandLine.ConfigFile != null
                ? new DeserializerBuilder()
                    .Build()
                    .Deserialize<List<Organization>>(await File.ReadAllTextAsync(commandLine.ConfigFile))
                : new List<Organization>();

            if (!string.IsNullOrEmpty(commandLine.User)
                && !string.IsNullOrEmpty(commandLine.Pat)
                && !string.IsNullOrEmpty(commandLine.ClonePath))
            {
                await Quantify(
                    organizations,
                    commandLine.ClonePath);
                await CloneAdoRepo.Program.Main(args);
            }

            await Quantify(commandLine.RepoPath);
        }

        private static async Task Quantify(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath))
            {
                return;
            }

            var fileInfoRepoPath = new FileInfo(repoPath);

            var quantifyClient = new QuantifyClient(string.Empty);

            var resultFile = Path.Combine(repoPath, $"{fileInfoRepoPath.Name}_QuantifierResults.csv");

            if (File.Exists(resultFile))
            {
                File.Delete(resultFile);
            }

            await InitializeResultFile(resultFile);

            await ComputeRepoStats(
                quantifyClient,
                resultFile,
                repoPath);
        }

        private static async Task Quantify(
            IEnumerable<Organization> organizations,
            string clonePath)
        {
            var repositories = organizations.Select(
                    o => o.Projects.Select(
                        p => p.Repositories.Select(
                            r => new
                            {
                                Organization = o.Name,
                                Project = p.Name,
                                Repository = r.Name
                            })))
                .SelectMany(a => a)
                .SelectMany(a => a);

            var quantifyClient = new QuantifyClient(string.Empty);

            foreach (var repository in repositories)
            {
                var resultFile = Path.Combine(clonePath, $"{repository.Repository}_QuantifierResults.csv");

                if (File.Exists(resultFile))
                {
                    File.Delete(resultFile);
                }

                await InitializeResultFile(resultFile);

                var repoPath = Path.Combine(clonePath, repository.Repository);
                await ComputeRepoStats(
                    quantifyClient,
                    resultFile,
                    repoPath);
            }
        }

        private static async Task InitializeResultFile(string csvResultPath)
        {
            await using var streamWriter = new StreamWriter(csvResultPath, true);
            await streamWriter.WriteLineAsync(
                "CommitSha1,QuantifiedLinesAdded,QuantifiedLinesDeleted,AbsoluteLinesAdded," +
                "AbsoluteLinesDeleted,PercentileAddition," +
                "PercentileDeletion,DiffPercentile,Label,TimeToMerge,AuthorEmail,AuthorName");
        }

        private static async Task AddResultsToFile(IReadOnlyDictionary<string, CommitStats> results, string csvResultPath)
        {
            await using var streamWriter = new StreamWriter(csvResultPath, true);
            foreach (var result in results)
            {
                await streamWriter.WriteLineAsync(
                    $"{result.Key}," +
                    $"{result.Value.QuantifiedLinesAdded}," +
                    $"{result.Value.QuantifiedLinesDeleted}," +
                    $"{result.Value.AbsoluteLinesAdded}," +
                    $"{result.Value.AbsoluteLinesDeleted}," +
                    $"{Math.Round(result.Value.PercentileAddition, 2)}," +
                    $"{Math.Round(result.Value.PercentileDeletion, 2)}," +
                    $"{Math.Round(result.Value.DiffPercentile, 2)}," +
                    $"{result.Value.Label}," +
                    $"{result.Value.TimeToMerge}," +
                    $"{result.Value.AuthorEmail}," +
                    $"{result.Value.AuthorName}");
            }
        }

        private static async Task ComputeRepoStats(
            QuantifyClient quantifyClient,
            string resultFile,
            string repoPath)
        {
            var repoRoot = LibGit2Sharp.Repository.Discover(repoPath);
            if (repoRoot == null)
            {
                Console.WriteLine($"No repo found at {repoPath}");
                return;
            }

            using var repo = new LibGit2Sharp.Repository(repoRoot);
            var commits = repo.Commits.QueryBy(
                new CommitFilter
                {
                    FirstParentOnly = true
                });

            Console.WriteLine($"Total commits to evaluate : {commits.Count()}. Repository path {repoPath}.");
            var sw = new Stopwatch();
            sw.Reset();
            sw.Start();
            var batchSize = 100;
            var commitStats = new ConcurrentDictionary<string, CommitStats>();
            for (int page = 0; page < (commits.Count() / batchSize) + 1; page++)
            {
                var commitBatch = commits.Skip(batchSize * page).Take(batchSize);
                var quantifyTasks = commitBatch.Select(
                    async commit =>
                    {
                        try
                        {
                            // we don't quantify first commit to repo
                            if (!commit.Parents.Any())
                            {
                                return;
                            }

                            var quantifierResult = await ComputeQuantifierStats(
                                quantifyClient,
                                commit,
                                repo);

                            var timeToMerge = ComputeTimeToMerge(commit, repo);

                            var commitStat = new CommitStats
                            {
                                Label = quantifierResult.Label,
                                CommitSha1 = commit.Sha,
                                DiffPercentile = quantifierResult.FormulaPercentile,
                                PercentileAddition = quantifierResult.PercentileAddition,
                                PercentileDeletion = quantifierResult.PercentileDeletion,
                                AbsoluteLinesAdded = quantifierResult.QuantifierInput.Changes.Sum(c => c.AbsoluteLinesAdded),
                                AbsoluteLinesDeleted = quantifierResult.QuantifierInput.Changes.Sum(c => c.AbsoluteLinesDeleted),
                                QuantifiedLinesAdded = quantifierResult.QuantifiedLinesAdded,
                                QuantifiedLinesDeleted = quantifierResult.QuantifiedLinesDeleted,
                                TimeToMerge = timeToMerge,
                                AuthorEmail = commit.Author.Email,
                                AuthorName = commit.Author.Name
                            };

                            commitStats.TryAdd(commit.Sha, commitStat);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    });
                await Task.WhenAll(quantifyTasks);
                await AddResultsToFile(commitStats, resultFile);
                commitStats = new ConcurrentDictionary<string, CommitStats>();
                Console.WriteLine($"{page * batchSize}/{commits.Count()} {sw.Elapsed}");
            }
        }

        private static async Task<QuantifierResult> ComputeQuantifierStats(
            QuantifyClient quantifyClient,
            Commit commit,
            LibGit2Sharp.Repository repo)
        {
            var quantifierInput = new QuantifierInput();
            foreach (var parent in commit.Parents)
            {
                var patch = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);

                foreach (var gitFilePatch in patch.GetGitFilePatch())
                {
                    quantifierInput.Changes.Add(gitFilePatch);
                }
            }

            var quantifierResult = await quantifyClient.Compute(quantifierInput);
            return quantifierResult;
        }

        private static TimeSpan ComputeTimeToMerge(Commit commit, LibGit2Sharp.Repository repo)
        {
            var mergeCommitWhen = commit.Author.When;
            var firstCommitInTree = repo.Commits.QueryBy(
                new CommitFilter
                {
                    SortBy = CommitSortStrategies.Reverse,
                    IncludeReachableFrom = commit.Sha,
                    ExcludeReachableFrom = commit.Parents.First().Sha
                }).First();
            var firstCommitWhen = firstCommitInTree.Author.When;
            var timeToMerge = mergeCommitWhen - firstCommitWhen;
            return timeToMerge;
        }
    }
}
