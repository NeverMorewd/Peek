using LibVLCSharp.Shared;

namespace Peek.Core.Services;

public sealed class AudioPlayer : IDisposable
{
    private LibVLC? _libVlc;
    private volatile bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public MediaPlayer? MediaPlayer { get; private set; }

    public async Task VlcInitializeAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await Task.Run(() => 
            {
                _libVlc = new LibVLC();
                MediaPlayer = new MediaPlayer(_libVlc);
                _initialized = true;
            });

        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task PlayFileAsync(string filePath)
    {
        if (MediaPlayer is null)
            throw new InvalidOperationException("Call VlcInitializeAsync first.");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<EventArgs>? endHandler = null;
        EventHandler<EventArgs>? errorHandler = null;

        endHandler = (_, _) =>
        {
            MediaPlayer.EndReached -= endHandler;
            MediaPlayer.EncounteredError -= errorHandler;
            TryDeleteFile(filePath);
            tcs.TrySetResult(true);
        };

        errorHandler = (_, _) =>
        {
            MediaPlayer.EndReached -= endHandler;
            MediaPlayer.EncounteredError -= errorHandler;
            TryDeleteFile(filePath);
            tcs.TrySetException(new Exception("LibVLC playback error"));
        };

        MediaPlayer.EndReached += endHandler;
        MediaPlayer.EncounteredError += errorHandler;

        using var media = new Media(_libVlc, new Uri(filePath));
        MediaPlayer.Play(media);

        return tcs.Task;
    }

    public async Task PlayBytesAsync(byte[] mp3Bytes)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(tmp, mp3Bytes).ConfigureAwait(false);
        await PlayFileAsync(tmp).ConfigureAwait(false);
    }

    public void Stop() => MediaPlayer?.Stop();

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* intentionally swallowed */ }
    }

    public void Dispose()
    {
        MediaPlayer?.Stop();
        MediaPlayer?.Dispose();
        _libVlc?.Dispose();  // was missing null-check
        _initLock.Dispose();
    }
}