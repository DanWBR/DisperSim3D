#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// A source's <see cref="ReleaseSource3D.ReleaseRateProfile"/> as a
    /// <c>&lt;RateProfile&gt;&lt;P T="0" Rate="0.38"/&gt;...&lt;/RateProfile&gt;</c> child of its
    /// <c>Source</c> element, shared by the engine's and the WPF editor's project readers and writers.
    /// </summary>
    public static class ReleaseRateProfileXml
    {
        /// <summary>The element, or null when the source has no profile.</summary>
        public static XElement? Write(ReleaseSource3D src)
        {
            if (src.ReleaseRateProfile == null || src.ReleaseRateProfile.Count == 0) return null;
            var inv = CultureInfo.InvariantCulture;
            return new XElement("RateProfile", src.ReleaseRateProfile.Select(p => new XElement("P",
                new XAttribute("T", p.TimeS.ToString(inv)),
                new XAttribute("Rate", p.RateKgPerS.ToString(inv)))));
        }

        /// <summary>Reads the profile of a <c>Source</c> element into <paramref name="src"/>, in time order.</summary>
        public static void Read(XElement sourceElement, ReleaseSource3D src)
        {
            var el = sourceElement.Element("RateProfile");
            if (el == null) return;
            var inv = CultureInfo.InvariantCulture;
            src.ReleaseRateProfile = el.Elements("P")
                .Select(p => new ReleaseRatePoint
                {
                    TimeS = double.Parse((string?)p.Attribute("T") ?? "0", inv),
                    RateKgPerS = double.Parse((string?)p.Attribute("Rate") ?? "0", inv)
                })
                .OrderBy(p => p.TimeS)
                .ToList();
        }
    }
}
