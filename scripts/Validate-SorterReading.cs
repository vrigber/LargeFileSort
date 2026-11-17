using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Comparison;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Reading;

internal static class SorterReadingValidation
{
    private static readonly List<string> Cases = [];
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static async Task<int> Main()
    {
        try
        {
            ValidateParser();
            ValidateComparer();
            ValidateAllocations();
            await ValidateReaderAsync();
            Console.WriteLine($"Passed: {Cases.Count}");
            foreach (string name in Cases) Console.WriteLine(name);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ValidateParser()
    {
        RecordParser parser = new();
        (string Line, int Number, string Text)[] valid =
        [
            ("0. a", 0, "a"),
            ("2147483646. Maximum", 2147483646, "Maximum"),
            ("0000.  Text . End  ", 0, " Text . End  "),
            ("0012. Text. More", 12, "Text. More"),
            ("1. \t\u00A0x \u2003", 1, "\t\u00A0x \u2003"),
            ("9. \0", 9, "\0"),
            ("3. \uFEFF", 3, "\uFEFF"),
            ("2. 😀 Яблоко", 2, "😀 Яблоко"),
            ("1. \u200B", 1, "\u200B"),
            (new string('0', 10000) + "1. a", 1, "a")
        ];
        foreach (var entry in valid)
        {
            SortRecord record = parser.Parse(entry.Line, "parser-input", 42);
            Check(ReferenceEquals(record.Line, entry.Line), "Parser copied the original string.");
            Check(record.Number == entry.Number, "Incorrect number.");
            Check(record.TextOffset == entry.Line.Length - entry.Text.Length, "Incorrect text offset.");
            Check(record.Line.AsSpan(record.TextOffset).SequenceEqual(entry.Text), "Text was changed.");
            Cases.Add($"Parser valid, number={entry.Number}, length={entry.Line.Length}");
        }

        string[] invalid =
        [
            "", " ", ". a", "1.a", "1. ", "1. \t\u00A0\u2003", "+1. x", "-0. x", "1,000. x",
            " 1. x", "1 . x", "١. x", "１. x", "2147483647. x", "2147483648. x",
            "999999999999999999999999. x", "1.0. x", "0\0. x", "\uFEFF1. x", "1.\tx", "1e3. x"
        ];
        foreach (string line in invalid)
        {
            var exception = Throws<InvalidDataException>(() => parser.Parse(line, "parser-input", 42));
            Check(exception.Message.Contains("parser-input") && exception.Message.Contains("line 42"), "Missing parse context.");
            Cases.Add($"Parser rejects invalid record {Cases.Count}");
        }
        Throws<ArgumentNullException>(() => parser.Parse(null, "parser-input", 1));
        Cases.Add("Parser rejects null argument");
    }

    private static void ValidateComparer()
    {
        RecordParser parser = new();
        SortRecordComparer comparer = new();
        (string Left, string Right, int Sign)[] pairs =
        [
            ("9. apple", "1. Banana", -1),
            ("2. APPLE", "10. apple", -1),
            ("0002. APPLE", "2. apple", 0),
            ("1. A", "2. a", -1),
            ("1. apple", "1. apple ", -1),
            ("1.  apple", "1. apple", -1),
            ("1. a.b", "1. a b", 1),
            ("1. Яблоко", "1. яблоко", 0),
            ("1. ä", "1. Ä", 0),
            ("1. e\u0301", "1. é", -1),
            ("2147483646. A", "0. a", 1),
            ("1. i", "1. I", 0)
        ];
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "tr-TR", "ru-RU" })
            {
                CultureInfo.CurrentCulture = new(culture);
                foreach (var pair in pairs)
                {
                    SortRecord left = parser.Parse(pair.Left, "left", 1);
                    SortRecord right = parser.Parse(pair.Right, "right", 1);
                    Check(Math.Sign(comparer.Compare(left, right)) == pair.Sign, $"Incorrect comparison in {culture}.");
                    Check(Math.Sign(comparer.Compare(right, left)) == -pair.Sign, "Comparison is not antisymmetric.");
                    Check(comparer.Compare(left, left) == 0, "Record is not equal to itself.");
                    Cases.Add($"Comparer {culture}: {pair.Left} / {pair.Right}");
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }

        Random random = new(42);
        string[] texts = ["Apple", "APPLE", "banana", " Banana", "banana ", "é", "e\u0301", "Я", "я"];
        List<(string Line, string Text, int Number)> input = [];
        for (int index = 0; index < 1000; index++)
        {
            string text = texts[random.Next(texts.Length)];
            int number = random.Next(20);
            input.Add(($"{number.ToString(index % 2 == 0 ? "D3" : "D", CultureInfo.InvariantCulture)}. {text}", text, number));
        }
        var records = input.Select(entry => parser.Parse(entry.Line, "generated", 1)).ToArray();
        var expected = input.OrderBy(entry => entry.Text, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Number).ToArray();
        Array.Sort(records, comparer);
        for (int index = 0; index < records.Length; index++)
        {
            Check(records[index].Number == expected[index].Number, "Numeric order differs from reference.");
            Check(records[index].Line.AsSpan(records[index].TextOffset).Equals(expected[index].Text, StringComparison.OrdinalIgnoreCase),
                "Text order differs from reference.");
        }
        var actualCounts = records.GroupBy(record => record.Line, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var expectedCounts = input.GroupBy(entry => entry.Line, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Check(actualCounts.Count == expectedCounts.Count && expectedCounts.All(entry => actualCounts.TryGetValue(entry.Key, out int count) && count == entry.Value),
            "Sorting changed the multiset of original lines.");
        Cases.Add("1000 seeded records: reference order and exact multiset");
    }

    private static void ValidateAllocations()
    {
        RecordParser parser = new();
        SortRecordComparer comparer = new();
        const string line = "000012. AbCd";
        SortRecord right = parser.Parse("9. abcd", "allocations", 1);
        for (int index = 0; index < 10000; index++) comparer.Compare(parser.Parse(line, "allocations", 1), right);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int index = 0; index < 10000; index++)
        {
            SortRecord record = parser.Parse(line, "allocations", 1);
            checksum += record.Number + comparer.Compare(record, right);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(checksum == 130000 && allocated == 0, $"Valid parser/comparer path allocated {allocated} bytes.");
        Cases.Add("Warmed parser/comparer: no managed allocations in 10000 valid operations");
    }

    private static async Task ValidateReaderAsync()
    {
        await ReadCaseAsync("Empty file", [], []);
        await ReadCaseAsync("BOM only", [0xEF, 0xBB, 0xBF], []);
        await ReadCaseAsync("Empty LF line", Utf8.GetBytes("\n"), [""]);
        await ReadCaseAsync("Blank lines are returned", Utf8.GetBytes("\n \r\t\r\n0. a"), ["", " ", "\t", "0. a"]);
        await ReadCaseAsync("Final LF adds no extra line", Utf8.GetBytes("a\n"), ["a"]);
        await ReadCaseAsync("Final line without terminator", Utf8.GetBytes("a"), ["a"]);
        foreach (int buffer in new[] { 1, 128, 129, 65536 })
        {
            const string content = "1. Apple\n2. Яблоко\r\n3. 😀\r4. End";
            string[] expected = ["1. Apple", "2. Яблоко", "3. 😀", "4. End"];
            await ReadCaseAsync($"UTF-8 without BOM, buffer={buffer}", Utf8.GetBytes(content), expected, buffer);
            await ReadCaseAsync($"UTF-8 with BOM, buffer={buffer}", [0xEF, 0xBB, 0xBF, .. Utf8.GetBytes(content)], expected, buffer);
        }
        foreach (int limit in new[] { 1, 2, 3 })
        {
            await ReadCaseAsync($"Short underlying reads={limit}", [0xEF, 0xBB, 0xBF, .. Utf8.GetBytes("😀\r\nЖ\n")], ["😀", "Ж"], maxReadBytes: limit);
        }
        string boundary = new('a', 127);
        await ReadCaseAsync("CRLF straddles byte buffer", Utf8.GetBytes(boundary + "\r\nb"), [boundary, "b"]);
        await ReadCaseAsync("Unicode straddles byte buffer", Utf8.GetBytes(boundary + "😀\r\nж"), [boundary + "😀", "ж"]);
        string longLine = new string('a', 100000) + "😀 Я";
        await ReadCaseAsync("Long line crosses many buffers", Utf8.GetBytes(longLine + "\nlast"), [longLine, "last"]);
        await ReadCaseAsync("BOM retained when disabled", [0xEF, 0xBB, 0xBF, .. Utf8.GetBytes("1. x")], ["\uFEFF1. x"], allowBom: false);
        await ReadCaseAsync("Only one initial BOM removed", [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, .. Utf8.GetBytes("1. x")], ["\uFEFF1. x"]);
        await ReadCaseAsync("BOM within content retained", Utf8.GetBytes("1. \uFEFF\n\uFEFF2. x"), ["1. \uFEFF", "\uFEFF2. x"]);

        byte[][] invalid =
        [
            [0xFF], [0xC0, 0xAF], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xF4, 0x90, 0x80, 0x80],
            [0xFF, 0xFE, 0x31, 0], [0xFE, 0xFF, 0, 0x31], [0xFF, 0xFE, 0, 0], [0, 0, 0xFE, 0xFF],
            [0xEF], [0xEF, 0xBB], [.. Utf8.GetBytes("1. Valid\n"), 0xFF]
        ];
        foreach (byte[] bytes in invalid)
        {
            TrackingStream stream = new(new MemoryStream(bytes, writable: false));
            await using (Utf8LineReader reader = new(stream, "invalid-utf8", 128))
            {
                var exception = await ThrowsAsync<InvalidDataException>(async () =>
                {
                    while (await reader.ReadLineAsync(CancellationToken.None) is not null) { }
                });
                Check(exception.Message.Contains("invalid-utf8") && exception.InnerException is DecoderFallbackException, "Missing UTF-8 context.");
                int reads = stream.AsyncReadCount;
                await ThrowsAsync<InvalidOperationException>(() => reader.ReadLineAsync(CancellationToken.None).AsTask());
                Check(stream.AsyncReadCount == reads, "Reader resumed after decoding error.");
            }
            Check(stream.AsyncDisposeCount == 1, "Invalid UTF-8 stream leaked.");
            Cases.Add("Strict UTF-8 rejects " + Convert.ToHexString(bytes));
        }

        await ValidateCancellationAsync();
        await ValidateOwnershipAndErrorsAsync();
        await ValidateBufferedReadsAsync();
        await ValidateFileAsync();
    }

    private static async Task ReadCaseAsync(string name, byte[] bytes, string[] expected, int buffer = 128, int maxReadBytes = int.MaxValue, bool allowBom = true)
    {
        TrackingStream stream = new(new MemoryStream(bytes, writable: false), maxReadBytes);
        await using (Utf8LineReader reader = new(stream, name, buffer, allowBom))
        {
            Check(reader.LineNumber == 0 && stream.AsyncReadCount == 0, "Reader performed I/O in constructor.");
            string first = null;
            for (int index = 0; index < expected.Length; index++)
            {
                string line = await reader.ReadLineAsync(CancellationToken.None);
                if (index == 0) first = line;
                Check(line == expected[index] && reader.LineNumber == index + 1, name + ": wrong line/number.");
            }
            Check(await reader.ReadLineAsync(CancellationToken.None) is null, name + ": missing EOF.");
            Check(await reader.ReadLineAsync(CancellationToken.None) is null && reader.LineNumber == expected.Length, name + ": EOF advanced line number.");
            if (expected.Length > 0) Check(first == expected[0], "Previously returned line changed.");
        }
        Check(stream.AsyncDisposeCount == 1 && stream.SyncReadCount == 0, name + ": ownership or async read violation.");
        Cases.Add(name);
    }

    private static async Task ValidateCancellationAsync()
    {
        TrackingStream beforeRead = new(new MemoryStream(Utf8.GetBytes("a\n")));
        using (CancellationTokenSource cancellation = new())
        await using (Utf8LineReader reader = new(beforeRead, "cancel-before", 128))
        {
            cancellation.Cancel();
            var exception = await ThrowsAsync<OperationCanceledException>(() => reader.ReadLineAsync(cancellation.Token).AsTask());
            Check(exception.CancellationToken == cancellation.Token && beforeRead.AsyncReadCount == 0 && reader.LineNumber == 0, "Pre-cancelled read performed I/O.");
            await ThrowsAsync<InvalidOperationException>(() => reader.ReadLineAsync(CancellationToken.None).AsTask());
        }
        Cases.Add("Cancellation before first read prevents I/O and resume");

        TrackingStream blocked = new(new MemoryStream()) { BlockReads = true };
        using (CancellationTokenSource cancellation = new())
        await using (Utf8LineReader reader = new(blocked, "cancel-during", 128))
        {
            Task<string> read = reader.ReadLineAsync(cancellation.Token).AsTask();
            await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            var exception = await ThrowsAsync<OperationCanceledException>(() => read);
            Check(exception.CancellationToken == cancellation.Token && reader.LineNumber == 0, "Cancellation token or count was lost.");
            await ThrowsAsync<InvalidOperationException>(() => reader.ReadLineAsync(CancellationToken.None).AsTask());
        }
        Check(blocked.AsyncDisposeCount == 1, "Cancelled stream leaked.");
        Cases.Add("Cancellation during pending async read propagates and closes");

        TrackingStream buffered = new(new MemoryStream(Utf8.GetBytes("one\ntwo\n")));
        using (CancellationTokenSource cancellation = new())
        await using (Utf8LineReader reader = new(buffered, "cancel-buffered", 128))
        {
            Check(await reader.ReadLineAsync(CancellationToken.None) == "one", "Wrong initial line.");
            int reads = buffered.AsyncReadCount;
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => reader.ReadLineAsync(cancellation.Token).AsTask());
            Check(buffered.AsyncReadCount == reads && reader.LineNumber == 1, "Buffered cancellation was ignored.");
            await ThrowsAsync<InvalidOperationException>(() => reader.ReadLineAsync(CancellationToken.None).AsTask());
        }
        Cases.Add("Cancellation checked even when next line is buffered");
    }

    private static async Task ValidateOwnershipAndErrorsAsync()
    {
        Throws<ArgumentNullException>(() => new Utf8LineReader(null, "input", 128));
        using (MemoryStream stream = new())
        {
            Throws<ArgumentException>(() => new Utf8LineReader(stream, " ", 128));
            Throws<ArgumentOutOfRangeException>(() => new Utf8LineReader(stream, "input", 0));
            Check(stream.CanRead, "Failed constructor took ownership.");
        }
        TrackingStream nonseekable = new(new MemoryStream()) { Seekable = false };
        Throws<ArgumentException>(() => new Utf8LineReader(nonseekable, "input", 128));
        Check(nonseekable.AsyncDisposeCount == 0, "Rejected stream disposed by reader.");
        await nonseekable.DisposeAsync();
        using (MemoryStream positioned = new([1, 2, 3]))
        {
            positioned.Position = 1;
            Throws<ArgumentException>(() => new Utf8LineReader(positioned, "input", 128));
        }
        MemoryStream closed = new();
        closed.Dispose();
        Throws<ArgumentException>(() => new Utf8LineReader(closed, "input", 128));
        Cases.Add("Reader validates stream, position, source and buffer before ownership");

        TrackingStream uninitialized = new(new MemoryStream());
        Utf8LineReader disposedReader = new(uninitialized, "dispose-before-read", 128);
        await disposedReader.DisposeAsync();
        await disposedReader.DisposeAsync();
        Check(uninitialized.AsyncDisposeCount == 1, "Dispose was not idempotent.");
        await ThrowsAsync<ObjectDisposedException>(() => disposedReader.ReadLineAsync(CancellationToken.None).AsTask());
        Cases.Add("Dispose before initialization is idempotent and prevents reads");

        IOException failure = new("Injected read failure.");
        TrackingStream failing = new(new MemoryStream(Utf8.GetBytes("a\n"))) { ReadError = failure };
        await using (Utf8LineReader reader = new(failing, "io-error", 128))
        {
            var exception = await ThrowsAsync<IOException>(() => reader.ReadLineAsync(CancellationToken.None).AsTask());
            Check(ReferenceEquals(exception, failure), "Original I/O error was changed.");
            await ThrowsAsync<InvalidOperationException>(() => reader.ReadLineAsync(CancellationToken.None).AsTask());
        }
        Check(failing.AsyncDisposeCount == 1, "Failed stream leaked.");
        Cases.Add("I/O error preserved, resume blocked, stream closed");

        IOException closeFailure = new("Injected close failure.");
        TrackingStream closing = new(new MemoryStream()) { DisposeError = closeFailure };
        Utf8LineReader closeReader = new(closing, "close-error", 128);
        var closeException = await ThrowsAsync<IOException>(() => closeReader.DisposeAsync().AsTask());
        Check(ReferenceEquals(closeException, closeFailure), "Close error was hidden.");
        await closeReader.DisposeAsync();
        Check(closing.AsyncDisposeCount == 1, "Close retried after error.");
        Cases.Add("Close error propagates without reopening or retrying");
    }

    private static async Task ValidateBufferedReadsAsync()
    {
        byte[] bytes = Utf8.GetBytes(string.Concat(Enumerable.Repeat("x\n", 1000)));
        TrackingStream stream = new(new MemoryStream(bytes, writable: false));
        await using (Utf8LineReader reader = new(stream, "buffering", 128))
        {
            while (await reader.ReadLineAsync(CancellationToken.None) is not null) { }
            Check(reader.LineNumber == 1000 && stream.AsyncReadCount < 100, "Line reads caused excessive underlying reads.");
        }
        Check(stream.SyncReadCount == 0, "Reader used synchronous I/O.");
        Cases.Add("1000 short lines served through buffered async stream reads");
    }

    private static async Task ValidateFileAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "utf8-input.txt");
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Utf8.GetBytes("0002. Apple\r\n1. 😀")];
        await File.WriteAllBytesAsync(path, bytes);
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using (Utf8LineReader reader = new(stream, path, 128))
        {
            Check(await reader.ReadLineAsync(CancellationToken.None) == "0002. Apple", "File BOM/CRLF handling failed.");
            Check(await reader.ReadLineAsync(CancellationToken.None) == "1. 😀", "File Unicode handling failed.");
            Check(await reader.ReadLineAsync(CancellationToken.None) is null && reader.LineNumber == 2, "File EOF handling failed.");
        }
        Check(!stream.CanRead && (await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(bytes), "File leaked or changed.");
        using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Cases.Add("Async FileStream integration: BOM, Unicode, unchanged bytes and released handle");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TrackingStream(Stream inner, int maxReadBytes = int.MaxValue) : Stream
    {
        public int AsyncReadCount { get; private set; }
        public int SyncReadCount { get; private set; }
        public int AsyncDisposeCount { get; private set; }
        public bool Seekable { get; init; } = true;
        public bool BlockReads { get; init; }
        public IOException ReadError { get; init; }
        public IOException DisposeError { get; init; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => Seekable && inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            AsyncReadCount++;
            if (ReadError is not null) throw ReadError;
            if (BlockReads)
            {
                ReadStarted.TrySetResult();
                return new(WaitForCancellationAsync(cancellationToken));
            }
            return inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxReadBytes)], cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            SyncReadCount++;
            return inner.Read(buffer, offset, Math.Min(count, maxReadBytes));
        }

        public override int Read(Span<byte> buffer)
        {
            SyncReadCount++;
            return inner.Read(buffer[..Math.Min(buffer.Length, maxReadBytes)]);
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
            if (DisposeError is not null) throw DisposeError;
        }

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
