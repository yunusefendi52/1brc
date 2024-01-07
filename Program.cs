using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Dumpify;
using SpanExtensions;
using csFastFloat;

unsafe class Program
{
    private static void Main(string[] args)
    {
        var sw = Stopwatch.StartNew();
        // var path = args.Length > 0 ? args[0] : "/Users/yunus/Documents/M/1brc/samples/measurements-25.txt";
        var path = args.Length > 0 ? args[0] : "/Users/yunus/Documents/M/1brc-challenge/java/1brc/measurements.txt";
        long getFileLength()
        {
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            return fileStream.Length;
        }
        var fileLength = getFileLength();
        using var mmap = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
        using var viewAccessor = mmap.CreateViewAccessor();
        byte* filePointer = null;
        viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref filePointer);
        var newLineChar = (byte)'\n';
        var chunks = GetChunks();
        var results = chunks
            .AsParallel()
            .Select(GetResult)
            .Aggregate(AggregateResult)
            .OrderBy(v => v.Key.ToString(), StringComparer.Ordinal).ToDictionary();
        PrintResults(results);
        sw.Dump();

        static Dictionary<DictionaryKey, ResultData> AggregateResult(Dictionary<DictionaryKey, ResultData> acum, Dictionary<DictionaryKey, ResultData> current)
        {
            foreach (var item in current)
            {
                ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(acum, item.Key, out var exists);
                if (exists)
                {
                    value.Calculate(item.Value);
                }
                else
                {
                    value = item.Value;
                }
            }
            return acum;
        }

        unsafe List<(long offset, long length)> GetChunks()
        {
            var chunkCount = Environment.ProcessorCount;
            var chunkSize = fileLength / chunkCount;
            var offset = 0L;
            var chunks = new List<(long offset, long length)>(chunkCount);
            for (int i = 0; i < chunkCount; i++)
            {
                var lineOffset = offset + chunkSize;
                var span = new Span<byte>(filePointer + lineOffset, 64); // TODO: Is this safe for finding the threshold?
                var lastNewLineIndex = span.IndexOf(newLineChar);
                var length = chunkSize + lastNewLineIndex;
                if (length + offset >= fileLength)
                {
                    length = fileLength - offset;
                }
                chunks.Add((offset, length));
                offset += length;
            }
            return chunks;
        }

        Dictionary<DictionaryKey, ResultData> GetResult((long offset, long length) v)
        {
            var result = new Dictionary<DictionaryKey, ResultData>(15000);
            var offset = 0L;
            while (true)
            {
                var fileOffset = v.offset + offset;
                if (fileOffset - v.offset >= v.length)
                {
                    break;
                }
                var span = new Span<byte>(filePointer + fileOffset, (int)v.length);
                var newLineIndex = span.IndexOf(newLineChar);
                if (newLineIndex == -1)
                {
                    break;
                }
                if (newLineIndex > 0)
                {
                    var currentLineSpan = span.Take(newLineIndex + 1);
                    var delimiterIndex = currentLineSpan.IndexOf((byte)';');
                    if (delimiterIndex != -1)
                    {
                        fixed (byte* namePointer = currentLineSpan[..delimiterIndex])
                        {
                            var name = new DictionaryKey(namePointer, delimiterIndex);
                            var value = FastDoubleParser.ParseDouble(currentLineSpan[(delimiterIndex + 1)..]);
                            ref var resultDict = ref CollectionsMarshal.GetValueRefOrAddDefault(result, name, out var exists);
                            resultDict.Calculate(value);
                        }
                    }
                }
                offset += newLineIndex + 1;
            }
            return result;
        }

        void PrintResults(Dictionary<DictionaryKey, ResultData> chunks)
        {
            var chunksCount = chunks.Count;
            var sb = new StringBuilder(chunksCount + 3);
            sb.Append('{');
            var line = 0;
            foreach (var item in chunks)
            {
                sb.Append($"{item.Key}={item.Value.Min:N1}/{item.Value.Mean:N1}/{item.Value.Max:N1}");
                line++;
                if (line < chunksCount)
                {
                    sb.Append(", ");
                }
            }
            sb.Append('}');
            Console.OutputEncoding = Encoding.UTF8;
            Console.Write(sb.ToString());
        }
    }
}

public struct ResultData
{
    public double Min { get; set; }
    public double Max { get; set; }
    public long Count { get; set; }
    public double Sum { get; set; }
    public readonly double Mean => Sum / Count;

    public void Calculate(double value)
    {
        if (value < Min)
            Min = value;
        else if (value > Max)
            Max = value;
        Sum += value;
        Count++;
    }

    public void Calculate(ResultData other)
    {
        if (other.Min < Min)
            Min = other.Min;
        if (other.Max > Max)
            Max = other.Max;
        Sum += other.Sum;
        Count += other.Count;
    }
}

unsafe struct DictionaryKey(byte* pointer, int length) : IEquatable<DictionaryKey>
{
    public readonly ReadOnlySpan<byte> Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(pointer, length);
    }

    public readonly bool Equals(DictionaryKey other)
    {
        return Value.SequenceEqual(other.Value);
    }

    public override readonly bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is DictionaryKey dictionaryKey && dictionaryKey.Equals(dictionaryKey);
    }

    public override readonly int GetHashCode()
    {
        if (length > 3)
        {
            return (length * 655360) ^ *(int*)pointer;
        }

        return *pointer;
    }

    public override readonly string ToString()
    {
        return Encoding.UTF8.GetString(Value);
    }
}
