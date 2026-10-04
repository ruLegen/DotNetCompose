using System;

namespace DotNetCompose.Runtime
{
    /// <summary>The latest result of a task or asynchronous sequence.</summary>
    public readonly struct AsyncState<T>
    {
        private readonly T _value;

        internal AsyncState(bool hasValue, T value, bool isRunning, bool isLoading, bool isCompleted, Exception? error)
        {
            HasValue = hasValue;
            _value = value;
            IsRunning = isRunning;
            IsLoading = isLoading;
            IsCompleted = isCompleted;
            Error = error;
        }

        public bool HasValue { get; }
        public T Value => HasValue ? _value : throw new InvalidOperationException("No asynchronous value is available.");
        public bool IsRunning { get; }
        public bool IsLoading { get; }
        public bool IsCompleted { get; }
        public Exception? Error { get; }

        internal static AsyncState<T> Initial(bool active) => new AsyncState<T>(false, default!, active, active, false, null);
        internal AsyncState<T> Begin() => new AsyncState<T>(HasValue, _value, true, true, false, null);
        internal AsyncState<T> Pause() => new AsyncState<T>(HasValue, _value, false, false, false, Error);
        internal AsyncState<T> WithValue(T value, bool completed) => new AsyncState<T>(true, value, !completed, false, completed, null);
        internal AsyncState<T> Complete(Exception? error = null) => new AsyncState<T>(HasValue, _value, false, false, true, error);
    }
}
