using System;

namespace DotNetCompose.Runtime.CompositionLocal
{
    /// <summary>
    /// A key for implicit data flow through the composition tree.
    /// Create via <see cref="CompositionLocalFactory.CompositionLocalOf{T}"/>
    /// or <see cref="CompositionLocalFactory.StaticCompositionLocalOf{T}"/>.
    /// </summary>
    public class CompositionLocal<T>
    {
        internal ValueHolder DefaultValueHolder { get; }

        internal CompositionLocal(Func<T> defaultFactory)
        {
            DefaultValueHolder = new LazyValueHolder(() => defaultFactory());
        }

        internal CompositionLocal(ValueHolder defaultValueHolder)
        {
            DefaultValueHolder = defaultValueHolder;
        }

        /// <summary>
        /// Gets the default value for this CompositionLocal when no provider is in scope.
        /// </summary>
        internal T GetDefaultValue()
        {
            var val = DefaultValueHolder.ReadValue(CompositionLocalMap.Empty);
            return val is T t ? t : default!;
        }
    }

    /// <summary>
    /// A CompositionLocal that can be provided via <see cref="CompositionLocalProvider"/>.
    /// </summary>
    public sealed class ProvidableCompositionLocal<T> : CompositionLocal<T>
    {
        internal bool IsDynamic { get; }

        internal ProvidableCompositionLocal(Func<T> defaultFactory, bool isDynamic)
            : base(defaultFactory)
        {
            IsDynamic = isDynamic;
        }

        internal ProvidableCompositionLocal(ValueHolder defaultValueHolder, bool isDynamic)
            : base(defaultValueHolder)
        {
            IsDynamic = isDynamic;
        }

        /// <summary>
        /// Associates this CompositionLocal with a value in CompositionLocalProvider.
        /// </summary>
        public ProvidedValue Provides(T value) =>
            new ProvidedValue(this, value, IsDynamic);

        /// <summary>
        /// Associates this CompositionLocal with a value that only applies
        /// if no other provider has set a value for this key.
        /// </summary>
        public ProvidedValue ProvidesDefault(T value) =>
            new ProvidedValue(this, value, IsDynamic).MarkAsDefaultOnly();
    }

    /// <summary>
    /// A pairing of a CompositionLocal key with its provided value.
    /// </summary>
    public sealed class ProvidedValue
    {
        internal object Key { get; }
        internal object? UntypedValue { get; }
        internal bool IsDefaultOnly { get; private set; }
        internal bool IsDynamic { get; }

        internal ProvidedValue(object key, object? untypedValue, bool isDynamic)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            UntypedValue = untypedValue;
            IsDynamic = isDynamic;
        }

        internal ProvidedValue MarkAsDefaultOnly()
        {
            IsDefaultOnly = true;
            return this;
        }
    }

    /// <summary>
    /// Factory methods for creating CompositionLocal keys.
    /// </summary>
    public static class CompositionLocalFactory
    {
        /// <summary>
        /// Creates a CompositionLocal backed by mutable state. Changing the provided value
        /// invalidates only the composables that read it.
        /// </summary>
        public static ProvidableCompositionLocal<T> CompositionLocalOf<T>(Func<T> defaultFactory)
        {
            if (defaultFactory == null)
                throw new ArgumentNullException(nameof(defaultFactory));
            return new ProvidableCompositionLocal<T>(defaultFactory, isDynamic: true);
        }

        /// <summary>
        /// Creates a CompositionLocal for values that rarely change. Changing the provided value
        /// invalidates the entire CompositionLocalProvider subtree.
        /// </summary>
        public static ProvidableCompositionLocal<T> StaticCompositionLocalOf<T>(Func<T> defaultFactory)
        {
            if (defaultFactory == null)
                throw new ArgumentNullException(nameof(defaultFactory));
            return new ProvidableCompositionLocal<T>(defaultFactory, isDynamic: false);
        }
    }
}
