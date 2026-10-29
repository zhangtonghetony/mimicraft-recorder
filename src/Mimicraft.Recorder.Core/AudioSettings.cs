namespace Mimicraft.Recorder.Core;

public sealed record AudioSettings(string Mode="none",string? SystemDevice=null,string? MicrophoneDevice=null,
    double SystemGain=1,double MicrophoneGain=1,int SampleRate=48000,int BitrateKbps=128,
    bool Enhance=false,bool Denoise=false,bool AudioOnly=false,string Format="wav",double NoiseThreshold=.008,double ReductionDb=12)
{
    public bool Enabled=>Mode!="none";
    public void Validate()
    {
        if(Mode is not ("none" or "system" or "microphone" or "both")||SampleRate is not (44100 or 48000)||BitrateKbps is not (64 or 128 or 192 or 256)||Format is not ("wav" or "m4a" or "mp3"))throw new ArgumentException("音频模式或输出参数无效。");
        if(!double.IsFinite(SystemGain+MicrophoneGain)||SystemGain is <0 or >4||MicrophoneGain is <0 or >4||AudioOnly&&!Enabled)throw new ArgumentException("音频增益或纯音频模式无效。");
        if(!double.IsFinite(NoiseThreshold+ReductionDb)||NoiseThreshold is <0 or >.05||ReductionDb is <3 or >24)throw new ArgumentException("降噪参数无效。");
    }
}
