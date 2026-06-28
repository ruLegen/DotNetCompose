using System.Collections.Generic;

namespace DotNetCompose.Runtime.Composer
{
    internal class CompositionContext
    {
        private readonly Dictionary<(object content, object? param), MovableContentStateReference> _removedMovableContent
            = new Dictionary<(object content, object? param), MovableContentStateReference>();

        public void InsertMovableContentState(MovableContentStateReference stateRef)
        {
            var key = (stateRef.Content, stateRef.Parameter);
            _removedMovableContent[key] = stateRef;
        }

        public MovableContentStateReference? TakeMovableContentState(MovableContent<object?> content, object? parameter)
        {
            var key = (content, parameter);
            if (_removedMovableContent.TryGetValue(key, out var stateRef))
            {
                _removedMovableContent.Remove(key);
                return stateRef;
            }
            return null;
        }

        public MovableContentStateReference? TakeMovableContentState(object content, object? parameter)
        {
            var key = (content, parameter);
            if (_removedMovableContent.TryGetValue(key, out var stateRef))
            {
                _removedMovableContent.Remove(key);
                return stateRef;
            }
            return null;
        }

        public void ClearUnused()
        {
            _removedMovableContent.Clear();
        }
    }
}
