using System;

namespace DisperSim3D.Models
{
    public class ObjectPlacedEventArgs : EventArgs
    {
        public EditMode PlacementType { get; set; }
        public object PlacedObject { get; set; }
    }
}
