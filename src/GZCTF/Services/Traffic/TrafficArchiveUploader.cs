using GZCTF.Services.Storage;

namespace GZCTF.Services.Traffic;

public sealed class TrafficArchiveUploader(TrafficArchiveStore store, IServiceScopeFactory scopes,
    ILogger<TrafficArchiveUploader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                store.Refresh();
                foreach (var path in Directory.EnumerateFiles(store.Root, "*.ready", SearchOption.AllDirectories))
                {
                    try
                    {
                        var relative = Path.GetRelativePath(store.Root, path).Replace('\\', '/');
                        var key = $"{S3StorageService.KeyPrefix}/{relative.Replace("/outbox/", "/")[..^6]}";
                        await using var scope = scopes.CreateAsyncScope();
                        var storage = scope.ServiceProvider.GetRequiredService<S3StorageService>();
                        await using (var stream = File.OpenRead(path))
                        {
                            var receipt = await storage.UploadVerifiedAsync(stream, key, "application/json", stoppingToken);
                            if (receipt is null)
                                continue;
                        }
                        // Retain a tiny index for downloads, never the uploaded log contents.
                        TrafficArchiveStore.WriteJson(path[..^6] + ".uploaded", new { uploadedAt = DateTimeOffset.UtcNow });
                        File.Delete(path);
                    }
                    catch (FileNotFoundException) { /* Another replica already finished this object. */ }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Traffic log upload failed; retaining {Path} for retry", path);
                    }
                }
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wake.CancelAfter(TimeSpan.FromSeconds(5));
                try { await store.Notifications.Reader.ReadAsync(wake.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Traffic archive outbox scan failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
