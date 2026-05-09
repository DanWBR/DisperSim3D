using System;
using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Project-wide defaults applied to new sources, wind fields, and simulations.
    /// Lives at the project root and is editable from the General Settings node of the project tree.
    /// </summary>
    public class ProjectSettings
    {
        [Category("Identity")]
        [Description("Project name shown as the root of the project tree.")]
        public string Name { get; set; }

        [Category("Identity")]
        [Description("Free-text description of the project.")]
        public string Description { get; set; }

        [Category("Identity")]
        [Description("Author or owner of the project.")]
        public string Author { get; set; }

        [Category("Identity")]
        [Description("Creation timestamp.")]
        public DateTime CreatedAt { get; set; }

        [Category("Defaults")]
        [Description("Default meteorological conditions used when creating new wind fields.")]
        public MeteorologicalConditions DefaultMeteo { get; set; }

        [Category("Defaults")]
        [Description("Default half-extent of the simulation box for new wind fields and simulations (m).")]
        public double DefaultDomainSizeM { get; set; }

        [Category("Defaults")]
        [Description("Default cell count per horizontal axis for new wind fields and simulations.")]
        public int DefaultGridResolution { get; set; }

        public ProjectSettings()
        {
            Name = "New Project";
            Description = "";
            Author = Environment.UserName ?? "";
            CreatedAt = DateTime.Now;
            DefaultMeteo = new MeteorologicalConditions();
            DefaultDomainSizeM = 200.0;
            DefaultGridResolution = 40;
        }
    }
}
