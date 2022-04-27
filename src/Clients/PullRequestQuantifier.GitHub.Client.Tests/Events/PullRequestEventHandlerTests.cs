﻿namespace PullRequestQuantifier.GitHub.Client.Tests.Events
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Octokit;
    using PullRequestQuantifier.Common;
    using PullRequestQuantifier.Common.Azure.Telemetry;
    using PullRequestQuantifier.GitHub.Client.Events;
    using PullRequestQuantifier.GitHub.Common.GitHubClient;
    using Xunit;

    [ExcludeFromCodeCoverage]
    public sealed class PullRequestEventHandlerTests
    {
        private readonly Mock<IGitHubClientAdapter> gitHubClientAdapter;
        private readonly Mock<IGitHubClientAdapterFactory> gitHubClientAdapterFactory;
        private readonly Mock<IAppTelemetry> appTelemetry;

        public PullRequestEventHandlerTests()
        {
            appTelemetry = new Mock<IAppTelemetry>();
            appTelemetry.Setup(f => f.RecordMetric(It.IsAny<string>(), It.IsAny<long>()));
            gitHubClientAdapter = new Mock<IGitHubClientAdapter>();
            gitHubClientAdapterFactory = new Mock<IGitHubClientAdapterFactory>();
            gitHubClientAdapterFactory.Setup(f => f.GetGitHubClientAdapterAsync(It.IsAny<long>(), It.IsAny<string>()))
                .ReturnsAsync(gitHubClientAdapter.Object);
            gitHubClientAdapter.Setup(f => f.GetPullRequestFilesAsync(It.IsAny<long>(), It.IsAny<int>()))
                .ReturnsAsync(new List<PullRequestFile>());
            gitHubClientAdapter.Setup(f => f.GetIssueLabelsAsync(It.IsAny<long>(), It.IsAny<int>()))
                .ReturnsAsync(new List<Label>());
            gitHubClientAdapter.Setup(f => f.GetIssueCommentsAsync(It.IsAny<long>(), It.IsAny<int>()))
                .ReturnsAsync(new List<IssueComment>());
        }

        [Fact]
        public async Task HandleEventTest()
        {
            // setup
            var installationEventHandler = new PullRequestEventHandler(
                gitHubClientAdapterFactory.Object,
                appTelemetry.Object,
                new Mock<ILogger<PullRequestEventHandler>>().Object);

            // act
            await installationEventHandler.HandleEvent(await File.ReadAllTextAsync(@"Data/PulRequestPayload.txt"));

            // assert
            appTelemetry.Verify(
                f =>
                    f.RecordMetric(It.IsAny<string>(), It.IsAny<long>()),
                Times.Once);
            Assert.Single(gitHubClientAdapter.Invocations.Where(i => i.Method.Name.Equals(nameof(gitHubClientAdapter.Object.GetRawFileAsync))));
        }

        [Fact]
        public async Task HandleEvent_WithGitHubFolderContext()
        {
            // setup
            gitHubClientAdapter.Setup(
                    g => g.GetRawFileAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.Is<string>(p => p.Equals($"/{Constants.RootContextFilePath}"))))
                .Throws(new NotFoundException("File not found", HttpStatusCode.NotFound));

            var installationEventHandler = new PullRequestEventHandler(
                gitHubClientAdapterFactory.Object,
                appTelemetry.Object,
                new Mock<ILogger<PullRequestEventHandler>>().Object);

            // act
            await installationEventHandler.HandleEvent(await File.ReadAllTextAsync(@"Data/PulRequestPayload.txt"));

            // assert
            appTelemetry.Verify(
                f =>
                    f.RecordMetric(It.IsAny<string>(), It.IsAny<long>()),
                Times.Once);
            Assert.Equal(2, gitHubClientAdapter.Invocations.Count(i => i.Method.Name.Equals(nameof(gitHubClientAdapter.Object.GetRawFileAsync))));
        }

        [Fact]
        public async Task HandleEvent_WithNoContext_DoesNotFail()
        {
            // setup
            gitHubClientAdapter.Setup(
                    g => g.GetRawFileAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                .Throws(new NotFoundException("File not found", HttpStatusCode.NotFound));

            var installationEventHandler = new PullRequestEventHandler(
                gitHubClientAdapterFactory.Object,
                appTelemetry.Object,
                new Mock<ILogger<PullRequestEventHandler>>().Object);

            // act
            await installationEventHandler.HandleEvent(await File.ReadAllTextAsync(@"Data/PulRequestPayload.txt"));

            // assert
            appTelemetry.Verify(
                f =>
                    f.RecordMetric(It.IsAny<string>(), It.IsAny<long>()),
                Times.Once);
            Assert.Equal(2, gitHubClientAdapter.Invocations.Count(i => i.Method.Name.Equals(nameof(gitHubClientAdapter.Object.GetRawFileAsync))));
        }
    }
}