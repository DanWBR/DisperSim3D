using System.Collections.ObjectModel;

namespace DisperSim3D.UI.Avalonia.ViewModels
{
    /// <summary>
    /// Hierarchical node model for the left-side project tree. The whole tree
    /// is rebuilt from a <see cref="DisperSim3D.Models.Scene3D"/> in
    /// <see cref="ProjectTreeBuilder.Build"/>; this type stays purely
    /// presentation-layer so it can be swapped for a richer ReactiveUI model
    /// later without touching the renderer side.
    /// </summary>
    public sealed class ProjectTreeNode
    {
        /// <summary>Display title shown in the tree row.</summary>
        public string Title { get; }

        /// <summary>Optional count badge ("N items"), rendered with reduced
        /// opacity to the right of the title. Empty string hides the badge.</summary>
        public string Badge { get; }

        /// <summary>Short emoji / unicode glyph used as a visual prefix.
        /// Cheap stand-in for proper icon assets until we wire SVG icons in.</summary>
        public string Icon { get; }

        /// <summary>The underlying domain object this node represents, e.g. a
        /// <see cref="DisperSim3D.Models.ReleaseSource3D"/>, a
        /// <see cref="DisperSim3D.Models.GasLibraryItem"/>, etc. Section nodes
        /// have <c>Tag == null</c>; only leaves carry domain objects.</summary>
        public object? Tag { get; }

        /// <summary>Stable identifier used to remember the selected node across
        /// rebuilds (right-click → add → tree refresh shouldn't lose focus).</summary>
        public string NodeId { get; }

        public ObservableCollection<ProjectTreeNode> Children { get; }
            = new ObservableCollection<ProjectTreeNode>();

        public ProjectTreeNode(string nodeId, string icon, string title,
            string badge = "", object? tag = null)
        {
            NodeId = nodeId;
            Icon = icon;
            Title = title;
            Badge = badge;
            Tag = tag;
        }
    }
}
