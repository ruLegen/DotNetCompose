using System;

namespace DotNetCompose.Runtime
{
    /// <summary>An observable condition under which UI work may run.</summary>
    public interface ITaskActivity
    {
        bool IsActive { get; }
        event EventHandler? Changed;
    }
}
