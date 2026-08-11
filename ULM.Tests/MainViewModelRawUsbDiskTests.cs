// ULM.Tests/MainViewModelRawUsbDiskTests.cs
using System.Windows.Threading;
using ULM.Core.Models;
using ULM.ViewModels;
using Xunit;

namespace ULM.Tests
{
    public class MainViewModelRawUsbDiskTests
    {
        private static (MainViewModel vm, FakeUsbService usb) Build()
        {
            var usb = new FakeUsbService();
            var vm  = new MainViewModel(Dispatcher.CurrentDispatcher, usb: usb);
            return (vm, usb);
        }

        [Fact]
        public void CheckRawUsbDisks_NewCandidate_CallsPrepareWithFreeLetter()
        {
            var (vm, usb) = Build();
            usb.RawDisksToReturn = new() { new RawUsbDiskCandidate(DiskIndex: 3, SizeBytes: 32_000_000_000) };

            vm.CheckRawUsbDisks();

            Assert.Single(usb.PrepareCalls);
            Assert.Equal(3, usb.PrepareCalls[0].DiskIndex);
        }

        [Fact]
        public void CheckRawUsbDisks_SameCandidateTwice_OnlyPreparedOnce()
        {
            var (vm, usb) = Build();
            usb.RawDisksToReturn = new() { new RawUsbDiskCandidate(3, 32_000_000_000) };

            vm.CheckRawUsbDisks();
            vm.CheckRawUsbDisks();

            Assert.Single(usb.PrepareCalls);
        }

        [Fact]
        public void CheckRawUsbDisks_NoCandidates_DoesNotCallPrepare()
        {
            var (vm, usb) = Build();
            usb.RawDisksToReturn = new();

            vm.CheckRawUsbDisks();

            Assert.Empty(usb.PrepareCalls);
        }

        [Fact]
        public void CheckRawUsbDisks_PrepareFails_DoesNotThrow()
        {
            var (vm, usb) = Build();
            usb.RawDisksToReturn = new() { new RawUsbDiskCandidate(3, 32_000_000_000) };
            usb.PrepareShouldSucceed = false;

            var ex = Record.Exception(() => vm.CheckRawUsbDisks());

            Assert.Null(ex);
        }
    }
}
