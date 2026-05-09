using System;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Project-wide defaults applied to new sources, wind fields, and simulations.
    /// Lives at the project root and is editable from the General Settings node of the project tree.
    /// </summary>
    public class ProjectSettings
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public DateTime CreatedAt { get; set; }

        public MeteorologicalConditions DefaultMeteo { get; set; }
        public double DefaultDomainSizeM { get; set; }
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
