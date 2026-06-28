namespace DotNetCompose.Runtime.Effects
{
    /// <summary>
    /// Receives lifecycle callbacks for objects stored via <c>remember</c>
    /// in the composition.
    /// </summary>
    public interface IRememberObserver
    {
        /// <summary>
        /// Called after the composition has committed and the object is active.
        /// </summary>
        void OnRemembered();

        /// <summary>
        /// Called when the object is removed from the composition (key changed
        /// or composable left the tree).
        /// </summary>
        void OnForgotten();

        /// <summary>
        /// Called when the composition was abandoned before the object's
        /// remembered state was committed.
        /// </summary>
        void OnAbandoned();
    }
}
