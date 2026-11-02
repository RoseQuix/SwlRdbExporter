using RdbExporter.Entities;
using RdbExporter.Parsers;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Particle effects (1010207) as JSON: systems, mesh systems, emitters, particles, textures, meshes, sound scripts,
    /// planes and decals. The few XML sources in the type are written as XML.
    /// </summary>
    public class ParticleEffectExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data) => ParticleEffectParser.Parse(ParticleEffectParser.Inflate(data));
    }
}
