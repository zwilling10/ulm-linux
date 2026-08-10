using System.Linq;
using ULM.Linux;
using Xunit;

namespace ULM.Linux.Tests
{
    public class LinuxUsbServiceParseLsblkJsonTests
    {
        private const string SampleJson = """
        {
           "blockdevices": [
              {"name":"sda","size":256060514304,"model":"Samsung SSD 970","rm":false,"type":"disk","mountpoint":null,
               "children": [
                  {"name":"sda1","size":536870912,"model":null,"rm":false,"type":"part","mountpoint":"/boot/efi"},
                  {"name":"sda2","size":255521931264,"model":null,"rm":false,"type":"part","mountpoint":"/"}
               ]
              },
              {"name":"sdb","size":16008609792,"model":"Kingston DataTraveler","rm":true,"type":"disk","mountpoint":null,
               "children": [
                  {"name":"sdb1","size":8388608,"model":null,"rm":true,"type":"part","mountpoint":null},
                  {"name":"sdb2","size":15998607360,"model":null,"rm":true,"type":"part","mountpoint":"/media/max/VENTOY"}
               ]
              },
              {"name":"sdc","size":4000000000,"model":"Generic Flash Disk","rm":true,"type":"disk","mountpoint":null},
              {"name":"sdd","size":8000000000,"model":"Swap Stick","rm":true,"type":"disk","mountpoint":"[SWAP]"}
           ]
        }
        """;

        [Fact]
        public void ParseLsblkJson_IgnoresNonRemovableDisks()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            Assert.DoesNotContain(devices, d => d.DeviceNode == "/dev/sda");
        }

        [Fact]
        public void ParseLsblkJson_ReturnsRemovableDisksWithDevPrefix()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            Assert.Contains(devices, d => d.DeviceNode == "/dev/sdb");
            Assert.Contains(devices, d => d.DeviceNode == "/dev/sdc");
        }

        [Fact]
        public void ParseLsblkJson_PicksMountPointOfLargestChildPartition()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdb = devices.Single(d => d.DeviceNode == "/dev/sdb");
            Assert.Equal("/media/max/VENTOY", sdb.MountPoint);
        }

        [Fact]
        public void ParseLsblkJson_DeviceWithoutChildren_HasNullMountPoint()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdc = devices.Single(d => d.DeviceNode == "/dev/sdc");
            Assert.Null(sdc.MountPoint);
        }

        [Fact]
        public void ParseLsblkJson_ReadsSizeAndModel()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdb = devices.Single(d => d.DeviceNode == "/dev/sdb");
            Assert.Equal(16008609792L, sdb.SizeBytes);
            Assert.Equal("Kingston DataTraveler", sdb.Model);
        }

        [Fact]
        public void ParseLsblkJson_SwapMountpoint_TreatedAsNotMounted()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdd = devices.Single(d => d.DeviceNode == "/dev/sdd");
            Assert.Null(sdd.MountPoint);
        }
    }

    public class LinuxUsbServiceListRemovableDevicesTests
    {
        [Fact]
        public async System.Threading.Tasks.Task ListRemovableDevicesAsync_UsesInjectedRunner()
        {
            const string json = """{"blockdevices":[{"name":"sdb","size":16008609792,"model":"Test","rm":true,"type":"disk","mountpoint":null}]}""";
            var service = new LinuxUsbService(() => System.Threading.Tasks.Task.FromResult(json));

            var devices = await service.ListRemovableDevicesAsync();

            Assert.Single(devices);
            Assert.Equal("/dev/sdb", devices[0].DeviceNode);
        }
    }
}
