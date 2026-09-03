using System.Collections.Generic;
using ULM.Core.Models;
using ULM.Linux.Views;
using Xunit;

namespace ULM.Linux.Tests
{
    public class IsoEditDialogTests
    {
        [Fact]
        public void ValidateEntry_EmptyName_ReturnsError()
        {
            var entry = new IsoEntry();
            string? error = IsoEditDialog.ValidateEntry("", "ubuntu.iso", entry, new List<IsoEntry> { entry });
            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateEntry_EmptyFilename_ReturnsError()
        {
            var entry = new IsoEntry();
            string? error = IsoEditDialog.ValidateEntry("Ubuntu", "", entry, new List<IsoEntry> { entry });
            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateEntry_DuplicateNameOnOtherEntry_ReturnsError()
        {
            var current = new IsoEntry { Name = "Original" };
            var other = new IsoEntry { Name = "Ubuntu 24.04 LTS" };
            string? error = IsoEditDialog.ValidateEntry("Ubuntu 24.04 LTS", "ubuntu.iso", current, new List<IsoEntry> { current, other });
            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateEntry_SameNameAsSelf_IsAllowed()
        {
            var current = new IsoEntry { Name = "Ubuntu 24.04 LTS" };
            string? error = IsoEditDialog.ValidateEntry("Ubuntu 24.04 LTS", "ubuntu.iso", current, new List<IsoEntry> { current });
            Assert.Null(error);
        }

        [Fact]
        public void ValidateEntry_ValidNewName_ReturnsNull()
        {
            var current = new IsoEntry { Name = "Original" };
            var other = new IsoEntry { Name = "Something Else" };
            string? error = IsoEditDialog.ValidateEntry("Renamed", "ubuntu.iso", current, new List<IsoEntry> { current, other });
            Assert.Null(error);
        }
    }
}
