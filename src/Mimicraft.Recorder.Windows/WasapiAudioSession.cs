using System.IO.Pipes;
using System.Threading.Channels;
using Mimicraft.Recorder.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Mimicraft.Recorder.Windows;

public sealed record AudioDevice(string Id,string Name);

public sealed class WasapiAudioSession:IDisposable
{
    private sealed class Input:IDisposable
    {
        public readonly WasapiCapture Capture;public readonly BufferedWaveProvider Buffer;public readonly ISampleProvider Samples;
        private readonly MMDevice _device;
        public Exception? Failure;private bool _closing;public volatile bool Paused;
        public Input(MMDevice device,bool loopback)
        {
            _device=device;
            Capture=loopback?new WasapiLoopbackCapture(device):new WasapiCapture(device);
            Buffer=new(Capture.WaveFormat){BufferDuration=TimeSpan.FromSeconds(3),ReadFully=true,DiscardOnBufferOverflow=false};
            ISampleProvider samples=Buffer.ToSampleProvider();
            if(samples.WaveFormat.Channels==1)samples=new MonoToStereoSampleProvider(samples);
            if(samples.WaveFormat.Channels!=2)throw new NotSupportedException("请选择单声道或立体声设备，暂不支持环绕声输入。");
            Samples=samples.WaveFormat.SampleRate==48000?samples:new WdlResamplingSampleProvider(samples,48000);
            Capture.DataAvailable+=(_,e)=>{try{if(!_closing&&!Paused)Buffer.AddSamples(e.Buffer,0,e.BytesRecorded);}catch(Exception error){Failure=error;}};
            Capture.RecordingStopped+=(_,e)=>{if(!_closing)Failure=e.Exception??new IOException("音频设备意外停止。");};
            Capture.StartRecording();
        }
        public void Dispose(){_closing=true;Capture.StopRecording();Capture.Dispose();_device.Dispose();}
    }
    private readonly List<(Input Input,double Gain,bool Mic)> _inputs=[];
    private readonly AudioSettings _settings;
    private readonly Rf64Backup _backup;
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _stop=new();
    private readonly Channel<byte[]> _blocks=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(240){SingleReader=true,SingleWriter=true});
    private readonly Task _writer;
    private bool _disposed;
    private long _sampleFrames;
    private Exception? _pipeFailure;
    private readonly float[] _previousIn=new float[2],_previousOut=new float[2];
    public string PipePath { get; }
    public string BackupPath { get; }
    public double Peak { get;private set; }
    public static IReadOnlyList<AudioDevice> Devices(bool microphone)
    {
        using var enumerator=new MMDeviceEnumerator();var devices=enumerator.EnumerateAudioEndPoints(microphone?DataFlow.Capture:DataFlow.Render,DeviceState.Active);
        var result=new List<AudioDevice>();foreach(var d in devices){result.Add(new(d.ID,d.FriendlyName));d.Dispose();}return result;
    }
    public WasapiAudioSession(AudioSettings settings,string backup)
    {
        settings.Validate();_settings=settings;BackupPath=backup;
        var pipeName="Mimicraft-audio-"+Guid.NewGuid().ToString("N");PipePath=@"\\.\pipe\"+pipeName;
        _pipe=new(pipeName,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536);
        _backup=new(backup);
        _writer=Task.Run(WritePipeAsync);
        try
        {
            using var enumerator=new MMDeviceEnumerator();
            if(settings.Mode is "system" or "both")Add(false,settings.SystemDevice,settings.SystemGain);
            if(settings.Mode is "microphone" or "both")Add(true,settings.MicrophoneDevice,settings.MicrophoneGain);
            void Add(bool microphone,string? id,double gain)
            {
                var device=string.IsNullOrWhiteSpace(id)?enumerator.GetDefaultAudioEndpoint(microphone?DataFlow.Capture:DataFlow.Render,Role.Multimedia):enumerator.GetDevice(id);
                _inputs.Add((new(device,!microphone),gain,microphone));
            }
        }
        catch{Dispose();throw;}
    }
    public void Arm(){foreach(var input in _inputs)input.Input.Buffer.ClearBuffer();}
    public void SetPaused(bool paused){foreach(var input in _inputs){input.Input.Paused=paused;input.Input.Buffer.ClearBuffer();}}
    public void WriteToVideoFrame(long videoFrames,int fps)
    {
        CheckHealth();
        int frames=checked((int)(videoFrames*48000/fps-_sampleFrames));if(frames<=0)return;
        var mixed=new float[frames*2];var scratch=new float[mixed.Length];
        foreach(var (input,gain,mic) in _inputs)
        {
            if(input.Failure is {} error)throw new IOException("音频设备不可用："+error.Message,error);
            Array.Clear(scratch);input.Samples.Read(scratch,0,scratch.Length);
            for(int i=0;i<scratch.Length;i++)
            {
                float sample=scratch[i];
                if(mic&&_settings.Enhance){int channel=i%2;float filtered=.9896f*(_previousOut[channel]+sample-_previousIn[channel]);_previousIn[channel]=sample;_previousOut[channel]=filtered;sample=filtered;}
                if(mic&&_settings.Denoise&&Math.Abs(sample)<_settings.NoiseThreshold)sample*=.15f;
                mixed[i]+=(float)(sample*gain);
            }
        }
        double peak=0;for(int i=0;i<mixed.Length;i++){mixed[i]=Math.Clamp(mixed[i],-1,1);peak=Math.Max(peak,Math.Abs(mixed[i]));}Peak=peak;
        var bytes=new byte[mixed.Length*4];Buffer.BlockCopy(mixed,0,bytes,0,bytes.Length);_backup.Write(bytes,0,bytes.Length);
        if(!_blocks.Writer.TryWrite(bytes))throw new IOException("音频写入落后，录制停止以避免音画漂移。");
        _sampleFrames+=frames;
    }
    public void CheckHealth()
    {
        if(_pipeFailure is {} failure)throw new IOException("音频编码管道失败。",failure);
        foreach(var item in _inputs)if(item.Input.Failure is {} e)throw new IOException("音频设备已停止："+e.Message,e);
    }
    private async Task WritePipeAsync()
    {
        try{await _pipe.WaitForConnectionAsync(_stop.Token);await foreach(var block in _blocks.Reader.ReadAllAsync(_stop.Token))await _pipe.WriteAsync(block,_stop.Token);await _pipe.FlushAsync(_stop.Token);}
        catch(OperationCanceledException)when(_stop.IsCancellationRequested){}
        catch(Exception e){_pipeFailure=e;}
    }
    public async Task CompleteAsync()
    {
        _blocks.Writer.TryComplete();await _writer.WaitAsync(TimeSpan.FromSeconds(15));_pipe.Dispose();_backup.Flush();
        if(_pipeFailure is {} failure)throw new IOException("音频写入未完成。",failure);
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_stop.Cancel();_blocks.Writer.TryComplete();var errors=new List<Exception>();
        foreach(var (input,_,_) in _inputs)try{input.Dispose();}catch(Exception e){errors.Add(e);}
        try{_pipe.Dispose();_writer.GetAwaiter().GetResult();}catch(Exception e){errors.Add(e);}
        try{_backup.Dispose();}catch(Exception e){errors.Add(e);}_stop.Dispose();if(errors.Count>0)throw new AggregateException("音频资源收尾出现错误。",errors);
    }
}
