using System;
using System.IO;

public static class PacketCodecTests
{
    public static int Run()
    {
        int checks=0;
        PacketWriter writer=new PacketWriter(); PacketReader reader=new PacketReader();
        Random random=new Random(912738);
        for(int sample=0;sample<10000;sample++)
        {
            int a=random.Next()*(sample%2==0?1:-1),b=random.Next();
            uint u=unchecked((uint)(random.Next()*(sample%2==0?1:-1)));
            ushort s=(ushort)random.Next(65536); byte v=(byte)random.Next(256); bool flag=sample%3==0;
            PacketFloat f=new PacketFloat(); f.bits=sample==0?unchecked((int)0x80000000):sample==1?0x7f800000:sample==2?0x7fc00123:random.Next();
            writer.Reset(); writer.Write(v);writer.Write(a);writer.Write(b);writer.Write(u);writer.Write(s);writer.Write(flag);writer.Write(f.value);
            byte[] expected;
            using(MemoryStream stream=new MemoryStream()) using(BinaryWriter old=new BinaryWriter(stream))
            {old.Write(v);old.Write(a);old.Write(b);old.Write(u);old.Write(s);old.Write(flag);old.Write(f.value);expected=stream.ToArray();}
            if(writer.Length!=expected.Length)throw new Exception("wire length");
            for(int i=0;i<expected.Length;i++)if(writer.Buffer[i]!=expected[i])throw new Exception("wire bytes sample="+sample+" index="+i);
            reader.Reset(writer.Buffer,writer.Length);
            if(reader.ReadByte()!=v||reader.ReadInt32()!=a||reader.ReadInt32()!=b||reader.ReadUInt32()!=u||reader.ReadUInt16()!=s||reader.ReadBoolean()!=flag)throw new Exception("wire integer decode");
            PacketFloat read=new PacketFloat();read.value=reader.ReadSingle();
            PacketFloat legacy=new PacketFloat();
            using(BinaryReader oldReader=new BinaryReader(new MemoryStream(expected,expected.Length-4,4))) legacy.value=oldReader.ReadSingle();
            // The 32-bit Mono JIT can normalize a NaN when returning a float.
            // Compare to BinaryReader on this same runtime, including that rule.
            if(read.bits!=legacy.bits)throw new Exception("wire float bits sample="+sample+" new="+read.bits+" legacy="+legacy.bits);
            bool truncated=false;try{reader.ReadByte();}catch(EndOfStreamException){truncated=true;}
            if(!truncated)throw new Exception("reader bounds");
            checks+=3;
        }
        writer.Reset();for(int i=0;i<10000;i++)writer.Write(i);
        if(writer.Length!=40000||writer.Buffer.Length<40000)throw new Exception("writer growth");
        reader.Reset(writer.Buffer,writer.Length);for(int i=0;i<10000;i++)if(reader.ReadInt32()!=i)throw new Exception("growth decode");
        byte[] storage=writer.Buffer;writer.Reset();writer.Write((byte)2);if(!object.ReferenceEquals(storage,writer.Buffer)||writer.Length!=1)throw new Exception("buffer reuse");
        checks+=3;
        return checks;
    }
    public static void Main(){Console.WriteLine("PACKET_CODEC PASS checks="+Run());}
}
