using Mimicraft.Recorder.Core;
using Xunit;

public class RemainingFeatureTests
{
    [Theory][InlineData("file:///C:/private.txt")][InlineData("https://example.com/file.mp4")][InlineData("rtmp://example.com/key|injected")][InlineData("rtmp://example.com/key\nmore")]
    public void InvalidPushAddressesAreRejected(string url)=>Assert.Throws<ArgumentException>(()=>StreamAddress.Validate(url));
    [Theory][InlineData("rtmp://127.0.0.1:1935/live/key")][InlineData("rtmps://example.com/app/stream?key=secret")][InlineData("udp://127.0.0.1:12345?pkt_size=1316")]
    public void StreamAddressesDoNotLeakPathOrQuery(string url){StreamAddress.Validate(url);Assert.DoesNotContain("secret",StreamAddress.Redact(url));Assert.DoesNotContain("/live/key",StreamAddress.Redact(url));}
    [Fact]public void AudioSettingsValidateModesAndLimits()
    {
        new AudioSettings("both",SystemGain:0,MicrophoneGain:4,AudioOnly:true,Format:"mp3").Validate();Assert.Throws<ArgumentException>(()=>new AudioSettings(AudioOnly:true).Validate());Assert.Throws<ArgumentException>(()=>new AudioSettings("system",SystemGain:double.NaN).Validate());Assert.Throws<ArgumentException>(()=>new AudioSettings("unknown").Validate());
    }
    [Fact]public void SceneLayerBoundsAndAlphaAreValidatedAndOrderIsObservable()
    {
        byte[] background=Enumerable.Repeat((byte)0,8*4).ToArray();byte[] red=[0,0,255,255],blue=[255,0,0,255];PixelBlit.Draw(background,4,2,red,1,1,new("image","red",0,0,1,1));PixelBlit.Draw(background,4,2,blue,1,1,new("image","blue",.5,0,.5,1));Assert.Equal(255,background[2]);Assert.Equal(255,background[8]);Assert.Throws<ArgumentException>(()=>new SceneLayer("image","x",.9,0,.2,1).Validate());
    }
    [Fact]public void ScenePresetSurvivesJsonRoundTripWithOrderingAndHiddenLayers()
    {
        var original=new ScenePreset("讲解","split",[new("camera","test-camera"),new("text","保留文字",Visible:false)],false);original.Validate();var copy=System.Text.Json.JsonSerializer.Deserialize<ScenePreset>(System.Text.Json.JsonSerializer.Serialize(original))!;copy.Validate();Assert.Equal(original.Name,copy.Name);Assert.False(copy.Layers[1].Visible);Assert.Equal("camera",copy.Layers[0].Kind);Assert.Equal("split",copy.Layout);
    }
}
