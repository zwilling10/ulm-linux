using System;
using System.IO;
using Xunit;

namespace ULM.Linux.Tests
{
    public class LinuxPathsTests
    {
        [Fact]
        public void ConfigDir_UsesXdgConfigHomeWhenSet()
        {
            string original = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/tmp/xdg-config-test");
                Assert.Equal(Path.Combine("/tmp/xdg-config-test", "ulm"), LinuxPaths.ConfigDir);
            }
            finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", original); }
        }

        [Fact]
        public void DataDir_UsesXdgDataHomeWhenSet()
        {
            string original = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/xdg-data-test");
                Assert.Equal(Path.Combine("/tmp/xdg-data-test", "ulm"), LinuxPaths.DataDir);
            }
            finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", original); }
        }

        [Fact]
        public void ConfigDir_FallsBackToHomeConfigWhenUnset()
        {
            string original = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "");
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Assert.Equal(Path.Combine(home, ".config", "ulm"), LinuxPaths.ConfigDir);
            }
            finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", original); }
        }

        [Fact]
        public void SettingsIni_IsUlmSettingsIniInsideConfigDir()
        {
            Assert.Equal(Path.Combine(LinuxPaths.ConfigDir, "ulm_settings.ini"), LinuxPaths.SettingsIni);
        }
    }
}
