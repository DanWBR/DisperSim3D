using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DisperSim3D.UI.Avalonia.ViewModels
{
    /// <summary>
    /// Hierarchical node model for the left-side project tree. The whole tree
    /// is rebuilt from a <see cref="DisperSim3D.Models.Scene3D"/> in
    /// <see cref="ProjectTreeBuilder.Build"/>; this type stays purely
    /// presentation-layer so it can be swapped for a richer ReactiveUI model
    /// later without touching the renderer side.
    /// </summary>
    public sealed class ProjectTreeNode : INotifyPropertyChanged
    {
        private bool _isVisible3D = true;

        /// <summary>Display title shown in the tree row.</summary>
        public string Title { get; }

        /// <summary>Optional count badge ("N items"), rendered with reduced
        /// opacity to the right of the title. Empty string hides the badge.</summary>
        public string Badge { get; }

        /// <summary>Optional status chip (e.g. simulation name for views).
        /// Empty string hides the chip.</summary>
        public string StatusText { get; }

        /// <summary>Hex colour for the status chip (foreground + tinted background).
        /// Default is gray.</summary>
        public string StatusColor { get; }

        /// <summary>Short emoji / unicode glyph used as a visual prefix.
        /// Cheap stand-in for proper icon assets until we wire SVG icons in.</summary>
        public string Icon { get; }

        /// <summary>Hex colour for the icon glyph. Default is gray.</summary>
        public string IconColor { get; }

        /// <summary>The underlying domain object this node represents, e.g. a
        /// <see cref="DisperSim3D.Models.ReleaseSource3D"/>, a
        /// <see cref="DisperSim3D.Models.GasLibraryItem"/>, etc. Section nodes
        /// have <c>Tag == null</c>; only leaves carry domain objects.</summary>
        public object? Tag { get; }

        /// <summary>Stable identifier used to remember the selected node across
        /// rebuilds (right-click → add → tree refresh shouldn't lose focus).</summary>
        public string NodeId { get; }

        /// <summary>Whether this node represents an object that can be shown/hidden
        /// in the 3D viewport. Section headers and non-spatial items are false.</summary>
        public bool HasVisibilityToggle { get; }

        /// <summary>Controls visibility of the corresponding 3D object in the
        /// viewport. Bound to a CheckBox in the tree item template.</summary>
        public bool IsVisible3D
        {
            get => _isVisible3D;
            set
            {
                if (_isVisible3D == value) return;
                _isVisible3D = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible3D)));
            }
        }

        public ObservableCollection<ProjectTreeNode> Children { get; }
            = new ObservableCollection<ProjectTreeNode>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProjectTreeNode(string nodeId, string icon, string title,
            string badge = "", object? tag = null, bool hasVisibilityToggle = false,
            bool initialVisibility = true,
            string statusText = "", string statusColor = "",
            string iconColor = "")
        {
            NodeId = nodeId;
            Icon = icon;
            Title = title;
            Badge = badge;
            Tag = tag;
            HasVisibilityToggle = hasVisibilityToggle;
            _isVisible3D = initialVisibility;
            StatusText = statusText;
            StatusColor = string.IsNullOrEmpty(statusColor) ? "#888888" : statusColor;
            IconColor = string.IsNullOrEmpty(iconColor) ? "#666666" : iconColor;
        }
    }
}
