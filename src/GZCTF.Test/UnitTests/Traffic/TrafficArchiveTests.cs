using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using GZCTF.Models.Data;
using GZCTF.Services.Storage;
using GZCTF.Services.Traffic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Context;
using Xunit;

namespace GZCTF.Test.UnitTests.Traffic;

public sealed class TrafficArchiveTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "gzctf-archive-test-" + Guid.NewGuid().ToString("N"));
    TrafficArchiveStore Store() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["TrafficCapture:ArchiveRoot"] = _root }).Build());

    TrafficCaptureSession Session() => new(Guid.NewGuid(), "epoch", "challenge-" + Guid.NewGuid(),
        1, 2, "test", "3", "team", Guid.NewGuid(), "starter", "192.0.2.1", DateTimeOffset.UtcNow.AddMinutes(-1));

    [Fact]
    public void PendingLogsAndSessionsSurviveRestart()
    {
        var first = Store();
        var session = Session();
        first.Register(session);
        first.Queue(session.Id, "logs/event.json", new { content = "wrong flag" });
        first.Stop(session.Id);
        var restarted = Store();
        Assert.Equal(session, restarted.Find(session.Id));
        Assert.True(restarted.IsStopped(session.Id));
        Assert.Contains("wrong flag", File.ReadAllText(Path.Combine(_root, session.Id.ToString("N"), "outbox", "logs", "event.json.ready")));
    }

    [Fact]
    public void ResultLogUsesSubmissionTimeActorAndIPAfterRestart()
    {
        var store = Store();
        var one = Session();
        var two = Session();
        store.Register(one);
        store.Register(two);
        var submission = new Submission { Id = 7, GameId = 1, ChallengeId = 2, TeamId = 3,
            SubmitTimeUtc = DateTimeOffset.UtcNow, User = new UserInfo { UserName = "submitter" } };
        store.RememberSubmission(submission, "198.51.100.42", [one.Pod, two.Pod]);
        store = Store();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(new TrafficArchiveSink(store)).CreateLogger();
        using (store.SubmissionLogScope(submission))
        using (LogContext.PushProperty("UserName", "changed-user"))
        using (LogContext.PushProperty("IP", IPAddress.Parse("203.0.113.1")))
            logger.Information("Incorrect flag: {Flag}", "wrong");
        foreach (var session in new[] { one, two })
        {
            var file = Directory.GetFiles(Path.Combine(_root, session.Id.ToString("N"), "outbox", "logs"), "*.ready").Single();
            using var log = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal("submitter", log.RootElement.GetProperty("user").GetString());
            Assert.Equal("198.51.100.42", log.RootElement.GetProperty("ip").GetString());
            Assert.Contains("Incorrect flag", log.RootElement.GetProperty("content").GetString());
            Assert.Equal("flag_result", log.RootElement.GetProperty("kind").GetString());
        }
    }

    [Fact]
    public void NewUncapturedLaunchDoesNotInheritOldCaptureLogs()
    {
        var store = Store();
        var session = Session();
        store.Register(session);
        var submission = new Submission { Id = 8, GameId = 1, ChallengeId = 2, TeamId = 3,
            SubmitTimeUtc = DateTimeOffset.UtcNow };
        store.RememberSubmission(submission, "192.0.2.1", ["new-uncaptured-pod"]);
        Assert.False(File.Exists(Path.Combine(_root, "submissions", "8.json")));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task UploadRequiresFullStoredHashAndSize(bool corrupt, bool wrongSize, bool expected)
    {
        var client = DispatchProxy.Create<IAmazonS3, FakeS3>();
        var fake = (FakeS3)(object)client;
        fake.Corrupt = corrupt;
        fake.WrongSize = wrongSize;
        var storage = new TestStorage(client);
        await using var local = new MemoryStream(Encoding.UTF8.GetBytes("pcap payload"));
        var receipt = await storage.UploadVerifiedAsync(local, "traffic/test.pcap", "application/vnd.tcpdump.pcap");
        Assert.Equal(expected, receipt is not null);
        Assert.True(local.CanRead);
        Assert.Equal(1, fake.PutCount);
        if (expected) Assert.Equal(12, receipt!.Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutboxDeletesContentsOnlyAfterVerification(bool corrupt)
    {
        var store = Store();
        var id = Guid.NewGuid();
        store.Queue(id, "logs/event.json", new { content = "original log" });
        var path = Path.Combine(_root, id.ToString("N"), "outbox", "logs", "event.json.ready");
        var index = path[..^6] + ".uploaded";
        var client = DispatchProxy.Create<IAmazonS3, FakeS3>();
        var fake = (FakeS3)(object)client;
        fake.Corrupt = corrupt;
        await using var provider = new ServiceCollection().AddSingleton<S3StorageService>(new TestStorage(client)).BuildServiceProvider();
        using var uploader = new TrafficArchiveUploader(store, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TrafficArchiveUploader>.Instance);
        await uploader.StartAsync(CancellationToken.None);
        try
        {
            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < timeout && (corrupt ? fake.VerifyCount == 0 : !File.Exists(index)))
                await Task.Delay(10);
        }
        finally { await uploader.StopAsync(CancellationToken.None); }
        Assert.True(fake.VerifyCount > 0);
        Assert.Equal(corrupt, File.Exists(path));
        Assert.Equal(!corrupt, File.Exists(index));
        if (corrupt) Assert.Contains("original log", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class TestStorage(IAmazonS3 client) : S3StorageService(null!, NullLogger<S3StorageService>.Instance)
    {
        public override Task<(IAmazonS3 Client, string Bucket)?> CreateClientAsync(CancellationToken token) =>
            Task.FromResult<(IAmazonS3 Client, string Bucket)?>((client, "bucket"));
    }

    public class FakeS3 : DispatchProxy
    {
        public bool Corrupt;
        public bool WrongSize;
        public int PutCount;
        public int VerifyCount;
        byte[] _bytes = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "PutObjectAsync":
                    PutCount++;
                    using (var copy = new MemoryStream())
                    {
                        ((PutObjectRequest)args![0]!).InputStream.CopyTo(copy);
                        _bytes = copy.ToArray();
                    }
                    return Task.FromResult(new PutObjectResponse());
                case "GetObjectMetadataAsync":
                    return Task.FromResult(new GetObjectMetadataResponse { ContentLength = _bytes.Length + (WrongSize ? 1 : 0) });
                case "GetObjectAsync":
                    Interlocked.Increment(ref VerifyCount);
                    var returned = _bytes.ToArray();
                    if (Corrupt) returned[0] ^= 1;
                    return Task.FromResult(new GetObjectResponse { ContentLength = returned.Length, ResponseStream = new MemoryStream(returned) });
                case "Dispose": return null;
                default: throw new NotSupportedException(method.Name);
            }
        }
    }
}
