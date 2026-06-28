using System;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public record class ComposableLambdaWrapper(Delegate Action)
    {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        public void Invoke(IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction).Invoke(composeContext, changed,defaultState);
        }
        public void Invoke<T0>(T0 arg0, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0>).Invoke(arg0, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1>(T0 arg0, T1 arg1, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1>).Invoke(arg0, arg1, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2>).Invoke(arg0, arg1, arg2, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3>).Invoke(arg0, arg1, arg2, arg3, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4>).Invoke(arg0, arg1, arg2, arg3, arg4, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, composeContext, changed,defaultState);
        }
        public void Invoke<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            (Action as ComposableAction<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>).Invoke(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, composeContext, changed,defaultState);
        }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
    }
}
