using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using ULM.Linux;
using Xunit;

namespace ULM.Linux.Tests
{
    public class VentoyInstallServiceEnsureScriptTests
    {
        private static byte[] BuildFakeVentoyTarGz(string scriptContent)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-fixture-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempDir, "ventoy-1.0.99"));
            string scriptPath = Path.Combine(tempDir, "ventoy-1.0.99", "Ventoy2Disk.sh");
            File.WriteAllText(scriptPath, scriptContent);

            string tarPath = Path.Combine(tempDir, "ventoy.tar");
            System.Formats.Tar.TarFile.CreateFromDirectory(Path.Combine(tempDir, "ventoy-1.0.99"), tarPath, includeBaseDirectory: true);

            string gzPath = tarPath + ".gz";
            using (var tarStream = File.OpenRead(tarPath))
            using (var gzStream = File.Create(gzPath))
            using (var gzip = new GZipStream(gzStream, CompressionMode.Compress))
                tarStream.CopyTo(gzip);

            byte[] bytes = File.ReadAllBytes(gzPath);
            Directory.Delete(tempDir, true);
            return bytes;
        }

        [Fact]
        public async Task EnsureVentoyScriptAsync_DownloadsAndExtractsScript()
        {
            byte[] tarGzBytes = BuildFakeVentoyTarGz("#!/bin/bash\necho fake ventoy script\n");
            string cacheDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-cache-{Guid.NewGuid():N}");

            var service = new VentoyInstallService(
                fetchLatestUrl: () => Task.FromResult("https://example.invalid/ventoy-1.0.99-linux.tar.gz"),
                download: async (url, destPath, progress, token) =>
                {
                    await File.WriteAllBytesAsync(destPath, tarGzBytes, token);
                    return true;
                },
                cacheDir: cacheDir);

            string scriptPath = await service.EnsureVentoyScriptAsync(onLog: null, CancellationToken.None);

            Assert.True(File.Exists(scriptPath));
            Assert.Equal("Ventoy2Disk.sh", Path.GetFileName(scriptPath));
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task EnsureVentoyScriptAsync_DownloadFails_ReturnsEmpty()
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-cache-{Guid.NewGuid():N}");
            var service = new VentoyInstallService(
                fetchLatestUrl: () => Task.FromResult("https://example.invalid/ventoy-1.0.99-linux.tar.gz"),
                download: (url, destPath, progress, token) => Task.FromResult(false),
                cacheDir: cacheDir);

            string scriptPath = await service.EnsureVentoyScriptAsync(onLog: null, CancellationToken.None);

            Assert.Equal(string.Empty, scriptPath);
        }
    }

    public class VentoyInstallServiceInstallOrUpdateTests
    {
        private static VentoyInstallService BuildServiceWithFakeScript(
            VentoyInstallService.RunElevatedFunc runElevated, out string cacheDir)
        {
            cacheDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(Path.Combine(cacheDir, "Ventoy2Disk.sh"), "#!/bin/bash\n");
            return new VentoyInstallService(
                fetchLatestUrl: () => Task.FromResult("https://example.invalid/ventoy.tar.gz"),
                download: (u, d, p, t) => Task.FromResult(true),
                runElevated: runElevated,
                cacheDir: cacheDir,
                // Kein echter udisksctl-Aufruf in Tests — deviceNode ist hier immer die
                // Literalzeichenkette "/dev/sdb", die auf dem Testrechner ein echtes,
                // eingehängtes Gerät sein könnte.
                unmount: (dev, log, ct) => Task.CompletedTask);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_Install_PassesInstallFlagAndDevice()
        {
            string? capturedArgs = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedArgs = args; return Task.FromResult((0, "")); },
                out string cacheDir);

            bool ok = await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.True(ok);
            Assert.Contains("-i", capturedArgs);
            Assert.Contains("/dev/sdb", capturedArgs);
            Assert.DoesNotContain("-s", capturedArgs);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_InstallWithSecureBoot_PassesSecureBootFlag()
        {
            string? capturedArgs = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedArgs = args; return Task.FromResult((0, "")); },
                out string cacheDir);

            await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: true, onLog: null, CancellationToken.None);

            Assert.Contains("-s", capturedArgs);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_UpdateMode_PassesUpdateFlagNotInstall()
        {
            string? capturedArgs = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedArgs = args; return Task.FromResult((0, "")); },
                out string cacheDir);

            await service.InstallOrUpdateAsync("/dev/sdb", updateMode: true, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.Contains("-u", capturedArgs);
            Assert.DoesNotContain("-i", capturedArgs);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_NonZeroExitCode_ReturnsFalse()
        {
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => Task.FromResult((1, "error")),
                out string cacheDir);

            bool ok = await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.False(ok);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_UsesPkexecAsCommand()
        {
            string? capturedCommand = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedCommand = cmd; return Task.FromResult((0, "")); },
                out string cacheDir);

            await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.Equal("pkexec", capturedCommand);
            Directory.Delete(cacheDir, true);
        }
    }
}
