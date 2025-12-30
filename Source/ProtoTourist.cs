using System;
using System.Collections.Generic;

namespace KourageousTourists
{
    public class ProtoTourist
    {
        public int Level { get; set; }
        public List<String> Abilities { get; set; }
        public List<String> Situations { get; set; }
        public List<String> CelestialBodies { get; set; }
        public double Srfspeed { get; set; }
        public bool IsSkydiver { get; set; }

        public ProtoTourist()
        {
            Abilities = new List<String>();
            Situations = new List<String>();
            CelestialBodies = new List<String>();
        }

        public override String ToString()
        {
            return String.Format("Tourist: < lvl={0}, abilities: [{1}], situations: [{2}], bodies: [{3}], speed: {4:F2}, skydiver: {5} >",
                Level,
                String.Join(", ", Abilities.ToArray()),
                String.Join(", ", Situations.ToArray()),
                String.Join(", ", CelestialBodies.ToArray()),
                Srfspeed, IsSkydiver);
        }
    }
}

