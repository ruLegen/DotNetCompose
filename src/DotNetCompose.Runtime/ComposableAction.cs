using System;
using System.Collections.Generic;
using System.Text;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public delegate void ComposableAction(IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1>(T1 arg1, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2>(T1 arg1, T2 arg2, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, IComposerContext composeContext, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState);

}
