namespace DotNetCompose.Runtime.Composer
{
    internal class FixupList
    {
        public Operations Operations { get; } = new Operations();

        public bool IsEmpty => Operations.IsEmpty;

        public void Clear() => Operations.Clear();
    }
}
