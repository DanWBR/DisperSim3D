using System;
using System.Collections.Generic;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a single gas component within a <see cref="GasMixture"/>, including its
    /// mole fraction and hazard thresholds.
    /// </summary>
    public class GasComponent
    {
        /// <summary>
        /// Gets or sets the unique identifier for this component.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the display name of the gas component.
        /// </summary>
        public string Name { get; set; } = "Component";

        /// <summary>
        /// Gets or sets the molar mass in kilograms per mole (kg/mol).
        /// </summary>
        public double MolarMass { get; set; } = 0.016;

        /// <summary>
        /// Gets or sets the mole fraction of this component in the mixture (0 to 1).
        /// </summary>
        public double MoleFraction { get; set; } = 1.0;

        /// <summary>
        /// Gets or sets the Lower Flammability Limit (kg/m³).
        /// </summary>
        public double LFL { get; set; }

        /// <summary>
        /// Gets or sets the Upper Flammability Limit (kg/m³).
        /// </summary>
        public double UFL { get; set; }

        /// <summary>
        /// Gets or sets the Immediately Dangerous to Life or Health concentration (kg/m³).
        /// </summary>
        public double IDLH { get; set; }
    }

    /// <summary>
    /// Represents a mixture of gas components, providing bulk property calculations
    /// and per-component concentration queries.
    /// </summary>
    public class GasMixture
    {
        /// <summary>
        /// Gets or sets the list of gas components in this mixture.
        /// </summary>
        public List<GasComponent> Components { get; set; } = new List<GasComponent>();

        /// <summary>
        /// Gets the mole-fraction-weighted average molar mass of the mixture in kg/mol.
        /// </summary>
        public double BulkMolarMass
        {
            get
            {
                double sum = 0;
                foreach (var c in Components)
                    sum += c.MoleFraction * c.MolarMass;
                return sum;
            }
        }

        /// <summary>
        /// Computes the concentration of a specific component given the total mixture concentration.
        /// </summary>
        /// <param name="componentIndex">The zero-based index of the component in <see cref="Components"/>.</param>
        /// <param name="totalConcentration">The total mixture concentration value.</param>
        /// <returns>The component concentration scaled by its mole fraction, or 0 if the index is out of range.</returns>
        public double GetComponentConcentration(int componentIndex, double totalConcentration)
        {
            if (componentIndex < 0 || componentIndex >= Components.Count) return 0;
            return totalConcentration * Components[componentIndex].MoleFraction;
        }
    }
}
