using NAudio.Wave;

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
