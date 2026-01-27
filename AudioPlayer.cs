using NAudio.Wave;
using LibVLCSharp.Shared;

namespace WinTuiPod;

internal sealed class AudioPlayer : IDisposable
{
    private IWavePlayer? _output;
    private WaveStream? _reader;
    private bool _isLiveStream;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => 
        _isLiveStream ? TimeSpan.Zero : _reader?.TotalTime ?? TimeSpan.Zero;

    public void PlayFile(string filePath)
    {
        Stop();

        _reader = new AudioFileReader(filePath);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.Play();
    }
    
    public void PlayStream(string streamUrl)
    {
        Stop();
        
        _isLiveStream = true;
        
        _reader = new MediaFoundationReader(streamUrl);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.Play();
    }

    public void TogglePause()
    {
        if (_output is null) return;

        if (_output.PlaybackState == PlaybackState.Playing) _output.Pause();
        else if (_output.PlaybackState == PlaybackState.Paused) _output.Play();
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;

        _reader?.Dispose();
        _reader = null;
    }

    public void SeekBy(TimeSpan delta)
    {
        if (_reader is null || _isLiveStream) return;

        var t = _reader.CurrentTime + delta;
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t > _reader.TotalTime) t = _reader.TotalTime;

        _reader.CurrentTime = t;
    }

    public void Dispose() => Stop();
}

internal sealed class RadioPlayer : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;

    public bool IsPlaying => _player.IsPlaying;

    public RadioPlayer()
    {
        Core.Initialize();
        _libVlc = new LibVLC("--no-video");
        _player = new MediaPlayer(_libVlc);
    }

    public void Play(string url)
    {
        using var media = new Media(_libVlc, new Uri(url));
        _player.Play(media);
    }
    
    public void TogglePause()
    {
        if (_player.IsPlaying)
            _player.Pause();
        else
            _player.Play();
    }

    public void Stop() => _player.Stop();

    public void Dispose()
    {
        _player.Dispose();
        _libVlc.Dispose();
    }
}

