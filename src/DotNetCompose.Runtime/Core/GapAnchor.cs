namespace DotNetCompose.Runtime
{
    internal class GapAnchor
    {
        public int Location { get; set; }

        public GapAnchor() { }

        public GapAnchor(int location)
        {
            Location = location;
        }

        public bool Valid => Location != int.MinValue;

        public void Invalidate() => Location = int.MinValue;
    }
}
