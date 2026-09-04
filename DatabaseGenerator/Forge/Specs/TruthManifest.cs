using System.Collections.Generic;

namespace DatabaseGenerator.Forge.Specs;

public sealed class TruthManifest
{
    public string Version { get; set; } = "1.0.0";
    public string ArtifactStatus { get; set; } = "validated";
    public string Scenario { get; set; } = "retail.customer_satisfaction";
    public int Seed { get; set; }
    public string GenerationEpoch { get; set; } = string.Empty;
    public string ProjectFingerprint { get; set; } = string.Empty;
    public string DatasetFingerprint { get; set; } = string.Empty;
    public SortedDictionary<string, long> SourceRowCounts { get; set; } = new(System.StringComparer.Ordinal);
    public SortedDictionary<string, long> ExpectedSilverRowCounts { get; set; } = new(System.StringComparer.Ordinal);
    public SortedDictionary<string, decimal> ExpectedKpis { get; set; } = new(System.StringComparer.Ordinal);
    public SortedDictionary<string, string> SourceFileSha256 { get; set; } = new(System.StringComparer.Ordinal);
    public List<TruthEvidence> Evidence { get; set; } = new();
    public ManifestInvariants Invariants { get; set; } = new();
}

public sealed class TruthEvidence
{
    public string EvidenceId { get; set; } = string.Empty;
    public string Injector { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public List<string> RecordKeys { get; set; } = new();
    public string ExpectedEffect { get; set; } = string.Empty;
    public SortedDictionary<string, string> Details { get; set; } = new(System.StringComparer.Ordinal);
}

public sealed class ManifestInvariants
{
    public bool Deterministic { get; set; } = true;
    public bool ForeignKeysValid { get; set; } = true;
    public string RawToLakeContract { get; set; } = "out/data/source is materialized byte-for-byte to lake/raw when --lake is supplied";
}
