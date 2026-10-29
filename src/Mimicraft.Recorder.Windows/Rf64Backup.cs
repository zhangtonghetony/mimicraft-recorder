using System.Text;

namespace Mimicraft.Recorder.Windows;

// RF64 keeps the recovery recording independent of RIFF's 4GB size ceiling.
public sealed class Rf64Backup:IDisposable
{
    private readonly FileStream _stream;private readonly BinaryWriter _writer;private long _bytes,_lastHeader;
    public Rf64Backup(string path)
    {
        _stream=new(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);_writer=new(_stream,Encoding.ASCII,true);
        void Tag(string value)=>_writer.Write(Encoding.ASCII.GetBytes(value));
        Tag("RF64");_writer.Write(uint.MaxValue);Tag("WAVE");Tag("ds64");_writer.Write(28u);_writer.Write(72UL);_writer.Write(0UL);_writer.Write(0UL);_writer.Write(0u);
        Tag("fmt ");_writer.Write(16u);_writer.Write((ushort)3);_writer.Write((ushort)2);_writer.Write(48000u);_writer.Write(384000u);_writer.Write((ushort)8);_writer.Write((ushort)32);Tag("data");_writer.Write(uint.MaxValue);_writer.Flush();
    }
    public void Write(byte[] data,int offset,int count){_stream.Write(data,offset,count);_bytes+=count;if(_bytes-_lastHeader>=384000)Flush();}
    public void Flush(){long end=_stream.Position;_stream.Position=20;_writer.Write((ulong)(_bytes+72));_writer.Write((ulong)_bytes);_writer.Write((ulong)(_bytes/8));_writer.Flush();_stream.Position=end;_stream.Flush();_lastHeader=_bytes;}
    public void Dispose(){Flush();_writer.Dispose();_stream.Dispose();}
}
