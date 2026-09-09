using Xunit;

namespace ULM.Linux.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void FakeIsoDatabaseService_StartsEmpty()
        {
            var db = new FakeIsoDatabaseService();
            Assert.Equal(0, db.Count);
        }
    }
}
