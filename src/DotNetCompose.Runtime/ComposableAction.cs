using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public delegate void ComposableAction(IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1>(T1 arg1, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2>(T1 arg1, T2 arg2, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, IComposeContext composeContext, int changed);

    // TODO Add changed2 arg here or replace int with long
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, IComposeContext composeContext, int changed);
    public delegate void ComposableAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, IComposeContext composeContext, int changed);

}
