using System.Collections.Generic;

namespace opentuner.Utilities
{
    public enum PropertyItemKind
    {
        Value = 0,
        Slider = 1,
        MediaControls = 2
    }

    public class PropertyItemDescriptor
    {
        public string Key;
        public string Name;
        public PropertyItemKind Kind;
        public int Min;
        public int Max;
    }

    public class PropertyGroupDescriptor
    {
        public int Id;
        public string Title;
        public List<PropertyItemDescriptor> Items = new List<PropertyItemDescriptor>();
    }

    public class PropertyMenuOption
    {
        public string Label;
        public int Command;
        public int[] Options;
        public bool Separator;
    }
}
