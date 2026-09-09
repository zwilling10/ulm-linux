using Xunit;

namespace ULM.Linux.Tests
{
    public class RelayCommandTests
    {
        [Fact]
        public void Execute_InvokesAction()
        {
            int calls = 0;
            var cmd = new RelayCommand(() => calls++);
            cmd.Execute(null);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void CanExecute_DefaultsToTrue()
        {
            var cmd = new RelayCommand(() => { });
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void CanExecute_UsesProvidedPredicate()
        {
            bool allowed = false;
            var cmd = new RelayCommand(() => { }, () => allowed);
            Assert.False(cmd.CanExecute(null));
            allowed = true;
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void RaiseCanExecuteChanged_FiresEvent()
        {
            var cmd = new RelayCommand(() => { });
            bool fired = false;
            cmd.CanExecuteChanged += (_, _) => fired = true;
            cmd.RaiseCanExecuteChanged();
            Assert.True(fired);
        }
    }
}
