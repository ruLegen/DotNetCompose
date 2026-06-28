using DotNetCompose.Runtime.Effects;

namespace DotNetCompose.Runtime.Composer
{
    internal class RememberObserverHolder
    {
        public IRememberObserver Observer { get; }

        public RememberObserverHolder(IRememberObserver observer)
        {
            Observer = observer;
        }
    }
}
