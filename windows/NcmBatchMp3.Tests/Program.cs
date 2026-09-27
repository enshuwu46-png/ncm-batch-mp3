using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NcmBatchMp3.Core;

internal static class Program
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CTENFDAM");
    private static readonly byte[] CoreKey = Convert.FromHexString("687A4852416D736F356B496E62617857");
    private static readonly byte[] MetadataKey = Convert.FromHexString("2331346C6A6B5F215C5D2630553C2728");
    private static readonly byte[] CoverPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFElEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    private static async Task Main()
    {
        AssertEqual(894, EasterEgg.DaysTogether(new DateTime(2026, 7, 12, 12, 0, 0)), "彩蛋计时");
        AssertEqual(
            "谨以此app，纪念Eric与Eva认识894天！",
            EasterEgg.Message(new DateTime(2026, 7, 12, 12, 0, 0)),
            "彩蛋文案");

        AssertTrue(UpdateRules.IsVersionNewer("v1.2.0", "1.1.2"), "新版比较");
        AssertTrue(!UpdateRules.IsVersionNewer("1.2.0", "1.2.0"), "同版比较");
        AssertTrue(!UpdateRules.IsVersionNewer("1.1.2", "1.2.0"), "旧版比较");
        AssertEqual(
            "https://github.com/enshuwu46-png/ncm-batch-mp3/releases/tag/v1.2.0",
            UpdateRules.OfficialReleaseUri(
                "v1.2.0",
                "https://github.com/enshuwu46-png/ncm-batch-mp3/releases/tag/v1.2.0")?.AbsoluteUri,
            "官方更新地址");
        AssertTrue(
            UpdateRules.OfficialReleaseUri("v1.2.0", "https://example.com/update") is null,
            "拦截非官方更新地址");

        var keyBox = NcmConverter.BuildKeyBox(Encoding.UTF8.GetBytes("test-stream-key"));
        var expectedPrefix = new byte[]
        {
            70, 218, 132, 64, 217, 166, 112, 195,
            68, 11, 211, 232, 95, 55, 88, 238,
            228, 34, 90, 131, 76, 19, 50, 174,
            108, 173, 40, 122, 251, 145, 35, 59
        };
        AssertTrue(keyBox.AsSpan(0, expectedPrefix.Length).SequenceEqual(expectedPrefix), "密钥盒算法");
        TestMaskBoundaries(keyBox);
        AssertEqual("_CON", NcmConverter.SafeFilename("CON"), "Windows 设备保留名");
        AssertEqual("_LPT1.song", NcmConverter.SafeFilename("LPT1.song"), "带扩展名的设备保留名");
        AssertEqual("converted", NcmConverter.SafeFilename("..."), "空白点文件名");
        var longTitle = string.Concat(Enumerable.Repeat("音乐😀", 100));
        var safeTitle = NcmConverter.SafeFilename(longTitle);
        AssertTrue(Encoding.UTF8.GetByteCount(safeTitle) <= 180 && !safeTitle.Contains('\ufffd'), "长 Unicode 文件名");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ncm-native-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "sample.ncm");
            var outputDirectory = Path.Combine(temporaryDirectory, "out");
            var expectedAudio = Encoding.Latin1.GetBytes("ID3\x04\x00\x00\x00\x00\x00\x10")
                .Concat(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("synthetic audio payload", 64))))
                .ToArray();
            await BuildSyntheticNcmAsync(sourcePath, expectedAudio, "mp3", "Synthetic Track", CoverPng);

            var converter = new NcmConverter();
            var result = await converter.ConvertAsync(
                sourcePath,
                new ConversionOptions(outputDirectory, OutputMode.PreferMp3, true, false),
                null,
                CancellationToken.None);

            AssertEqual("Codex - Synthetic Track.mp3", Path.GetFileName(result.OutputPath), "元数据命名");
            AssertTrue(File.ReadAllBytes(result.OutputPath).AsSpan().SequenceEqual(expectedAudio), "NCM 往返字节一致");

            var oversizedHeaderPath = Path.Combine(temporaryDirectory, "oversized-header.ncm");
            await BuildSyntheticNcmAsync(oversizedHeaderPath, expectedAudio, "mp3", "Oversized Header", CoverPng);
            var oversizedHeader = await File.ReadAllBytesAsync(oversizedHeaderPath);
            BitConverter.GetBytes((uint)(16 * 1024 * 1024 + 1)).CopyTo(oversizedHeader, 10);
            await File.WriteAllBytesAsync(oversizedHeaderPath, oversizedHeader);
            var rejectedOversizedHeader = false;
            try
            {
                await converter.ConvertAsync(
                    oversizedHeaderPath,
                    new ConversionOptions(outputDirectory, OutputMode.PreferMp3, false, false),
                    null,
                    CancellationToken.None);
            }
            catch (InvalidDataException)
            {
                rejectedOversizedHeader = true;
            }
            AssertTrue(rejectedOversizedHeader, "超大头部字段拒绝");
            var options = new ConversionOptions(outputDirectory, OutputMode.Original, true, true);
            var largePath = Path.Combine(temporaryDirectory, "large.ncm");
            var largeAudio = new byte[8 * 1024 * 1024 + 137];
            new Random(1234).NextBytes(largeAudio);
            "ID3"u8.CopyTo(largeAudio);
            await BuildSyntheticNcmAsync(largePath, largeAudio, "mp3", longTitle, []);
            var largeResult = await converter.ConvertAsync(largePath, options, null, CancellationToken.None);
            AssertTrue(File.ReadAllBytes(largeResult.OutputPath).AsSpan().SequenceEqual(largeAudio), "跨 4 MiB 分块及尾块一致");
            var originalHash = SHA256.HashData(File.ReadAllBytes(largeResult.OutputPath));
            using (var cancellation = new CancellationTokenSource())
            {
                var rejected = false;
                try
                {
                    await converter.ConvertAsync(largePath, options, null, cancellation.Token,
                        new InlineProgress(_ => cancellation.Cancel()));
                }
                catch (OperationCanceledException) { rejected = true; }
                AssertTrue(rejected, "解密中取消");
                AssertTrue(SHA256.HashData(File.ReadAllBytes(largeResult.OutputPath)).AsSpan().SequenceEqual(originalHash), "取消覆盖保留原文件");
            }
            AssertTrue(!Directory.EnumerateDirectories(outputDirectory, ".ncm-work-*").Any(), "工作目录已清理");
            AssertTrue(!Directory.EnumerateFiles(outputDirectory, ".ncm-batch-*").Any(), "暂存输出已清理");
            if (Environment.GetEnvironmentVariable("NCM_TEST_FFMPEG") is { Length: > 0 } ffmpeg)
            {
                await TestFfmpegAsync(temporaryDirectory, converter, ffmpeg);
            }
            Console.WriteLine("SIMD boundary, cancellation, overwrite and long filename checks ok");
            Console.WriteLine($"native core roundtrip ok: {Path.GetFileName(result.OutputPath)}");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static async Task BuildSyntheticNcmAsync(
        string filePath,
        byte[] audio,
        string audioFormat,
        string title,
        byte[] cover)
    {
        var keyData = Encoding.UTF8.GetBytes("test-stream-key");
        var keyPlain = Encoding.UTF8.GetBytes("neteasecloudmusic").Concat(keyData).ToArray();
        var encryptedKey = Xor(AesEncrypt(keyPlain, CoreKey), 0x64);
        var metadata = JsonSerializer.Serialize(new
        {
            musicName = title,
            artist = new object[] { new object[] { "Codex", 1 } },
            album = "Synthetic Album",
            format = audioFormat
        });
        var metadataPlain = Encoding.UTF8.GetBytes($"music:{metadata}");
        var metadataPayload = Encoding.UTF8.GetBytes("163 key(Don't modify):")
            .Concat(Encoding.UTF8.GetBytes(Convert.ToBase64String(AesEncrypt(metadataPlain, MetadataKey))))
            .ToArray();
        var encryptedMetadata = Xor(metadataPayload, 0x63);
        var encryptedAudio = EncryptAudio(audio, NcmConverter.BuildKeyBox(keyData));

        await using var output = File.Create(filePath);
        await output.WriteAsync(Magic);
        await output.WriteAsync(new byte[] { 0x02, 0x00 });
        await output.WriteAsync(BitConverter.GetBytes((uint)encryptedKey.Length));
        await output.WriteAsync(encryptedKey);
        await output.WriteAsync(BitConverter.GetBytes((uint)encryptedMetadata.Length));
        await output.WriteAsync(encryptedMetadata);
        await output.WriteAsync(new byte[5]);
        await output.WriteAsync(BitConverter.GetBytes((uint)(cover.Length + 3)));
        await output.WriteAsync(BitConverter.GetBytes((uint)cover.Length));
        await output.WriteAsync(cover);
        await output.WriteAsync(new byte[3]);
        await output.WriteAsync(encryptedAudio);
    }

    private static void TestMaskBoundaries(byte[] keyBox)
    {
        var audio = Enumerable.Range(0, 8192).Select(index => (byte)(index * 17 + 13)).ToArray();
        var encrypted = EncryptAudio(audio, keyBox);
        var mask = NcmConverter.BuildAudioMask(keyBox);
        for (var offset = 0; offset < 256; offset++)
        {
            foreach (var count in new[] { 0, 1, 15, 16, 17, 31, 32, 33, 255, 256, 257, 4099 })
            {
                var chunk = encrypted.AsSpan(offset, count).ToArray();
                NcmConverter.ApplyAudioMask(chunk, mask, offset);
                AssertTrue(chunk.AsSpan().SequenceEqual(audio.AsSpan(offset, count)), "非对齐分块解密");
            }
        }
    }

    private sealed class InlineProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => report(value);
    }

    private static async Task TestFfmpegAsync(string directory, NcmConverter converter, string ffmpeg)
    {
        var flac = Path.Combine(directory, "tone.flac");
        var cover = Path.Combine(directory, "cover.png");
        await RunFfmpegAsync(ffmpeg, "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", flac);
        await RunFfmpegAsync(ffmpeg, "-y", "-f", "lavfi", "-i", "color=c=white:s=64x64", "-frames:v", "1", cover);
        var source = Path.Combine(directory, "real-audio.ncm");
        await BuildSyntheticNcmAsync(source, File.ReadAllBytes(flac), "flac", "Real Audio", File.ReadAllBytes(cover));
        var options = new ConversionOptions(Path.Combine(directory, "ffmpeg-out"), OutputMode.PreferMp3, true, true);
        var result = await converter.ConvertAsync(source, options, ffmpeg, CancellationToken.None);
        var pcm = await RunFfmpegAsync(ffmpeg, "-i", result.OutputPath, "-map", "0:a:0", "-ar", "44100", "-ac", "1", "-f", "s16le", "-");
        AssertTrue(pcm.Length / 88200.0 >= 2.8, "C# ffmpeg 音频时长完整");
        var embedded = await RunFfmpegAsync(ffmpeg, "-i", result.OutputPath, "-map", "0:v:0", "-frames:v", "1", "-f", "image2pipe", "-");
        AssertTrue(embedded.Length > 3 && embedded[0] == 0xff && embedded[1] == 0xd8, "C# ffmpeg 封面嵌入");

        // On macOS the native C# core can still exercise a real, cancellable child process.
        if (!OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "slow-encoder.sh");
            var marker = Path.Combine(directory, "encoder-started");
            var quotedMarker = "'" + marker.Replace("'", "'\\''") + "'";
            await File.WriteAllTextAsync(script, $"#!/bin/sh\nprintf ready > {quotedMarker}\nexec /bin/sleep 30\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var protectedHash = SHA256.HashData(File.ReadAllBytes(result.OutputPath));
            using var cancellation = new CancellationTokenSource();
            var running = converter.ConvertAsync(source, options, script, cancellation.Token);
            try
            {
                var timeout = Stopwatch.StartNew();
                while (!File.Exists(marker) && timeout.Elapsed.TotalSeconds < 5 && !running.IsCompleted)
                    await Task.Delay(10);
                AssertTrue(File.Exists(marker), "测试编码进程已启动");
            }
            finally { cancellation.Cancel(); }
            var cancelled = false;
            try { await running.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) { cancelled = true; }
            AssertTrue(cancelled, "正在运行的编码进程及时取消");
            AssertTrue(SHA256.HashData(File.ReadAllBytes(result.OutputPath)).AsSpan().SequenceEqual(protectedHash), "取消转码不覆盖已有输出");
            AssertTrue(!Directory.EnumerateDirectories(options.OutputDirectory, ".ncm-work-*").Any(), "取消转码清理工作目录");
            AssertTrue(!Directory.EnumerateFiles(options.OutputDirectory, ".ncm-batch-*").Any(), "取消转码清理暂存文件");
        }
        Console.WriteLine("Native C# ffmpeg cover, playable audio and process cancellation checks ok");
    }

    private static async Task<byte[]> RunFfmpegAsync(string ffmpeg, params string[] arguments)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-v");
        info.ArgumentList.Add("error");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        using var output = new MemoryStream();
        var readOutput = process.StandardOutput.BaseStream.CopyToAsync(output);
        var readError = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(readOutput, readError, process.WaitForExitAsync());
        AssertTrue(process.ExitCode == 0, "ffmpeg fixture: " + await readError);
        return output.ToArray();
    }

    private static byte[] AesEncrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] Xor(byte[] data, byte value)
    {
        var output = data.ToArray();
        for (var index = 0; index < output.Length; index++)
        {
            output[index] ^= value;
        }
        return output;
    }

    private static byte[] EncryptAudio(byte[] data, byte[] keyBox)
    {
        var output = data.ToArray();
        for (var index = 0; index < output.Length; index++)
        {
            var j = (index + 1) & 0xff;
            var first = keyBox[j];
            var secondIndex = (first + j) & 0xff;
            var maskIndex = (first + keyBox[secondIndex]) & 0xff;
            output[index] ^= keyBox[maskIndex];
        }
        return output;
    }

    private static void AssertTrue(bool value, string name)
    {
        if (!value)
        {
            throw new InvalidOperationException($"测试失败：{name}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"测试失败：{name}，预期 {expected}，实际 {actual}");
        }
    }
}
