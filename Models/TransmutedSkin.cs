namespace TaimisToolbench.Models
{
    /// <summary>
    /// The name and the icon of the skin a stack wears, as one value.
    /// <para>
    /// They travel together so no caller can take one and not the other.
    /// A row showing one item's name over another item's picture is worse
    /// than a row showing the item's own name and picture, so a skin
    /// missing either half is not used at all: <see cref="Of"/> returns
    /// <see cref="None"/> unless both resolved.
    /// </para>
    /// </summary>
    internal sealed class TransmutedSkin
    {
        public static readonly TransmutedSkin None = new TransmutedSkin("", "");

        private TransmutedSkin(string name, string iconUrl)
        {
            Name = name;
            IconUrl = iconUrl;
        }

        public static TransmutedSkin Of(string name, string iconUrl)
        {
            return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(iconUrl)
                ? None
                : new TransmutedSkin(name, iconUrl);
        }

        public string Name { get; }

        public string IconUrl { get; }

        /// <summary>True when this stands for a skin the caller may draw.
        /// False on <see cref="None"/>, and there is no third case.</summary>
        public bool IsPresent => Name.Length > 0;
    }
}
