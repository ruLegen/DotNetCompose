namespace DotNetCompose.Runtime.Composer
{
    /// <summary>Lifecycle of physical nodes retained independently of remembered composition state.</summary>
    public interface IComposeNodeLifecycleCallback
    {
        /// <summary>Resets data associated with the previous content, before new node updates.</summary>
        void OnReuse();

        /// <summary>Stops work for inactive content while retaining the node for future reuse.</summary>
        void OnDeactivate();

        /// <summary>Releases data when the node permanently leaves its composition.</summary>
        void OnRelease();
    }
}
