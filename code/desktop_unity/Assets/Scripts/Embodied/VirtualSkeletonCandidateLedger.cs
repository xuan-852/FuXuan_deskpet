using System;
using System.Collections.Generic;

public enum SkeletonEvidenceStatus { Supporting, Conditional, Certified }
public sealed class VirtualSkeletonCandidate
{
    public string NodeId;
    public string ParentNodeId;
    public string EvidenceId;
    public string ModelEvidenceVersion;
    public SkeletonEvidenceStatus Status;
    public EmbodiedResource Resources;
    public bool IsRuntimeEligible => Status == SkeletonEvidenceStatus.Certified;
}

/// <summary>Offline evidence ledger. It is not a parameter map or runtime controller.</summary>
public sealed class VirtualSkeletonCandidateLedger
{
    private readonly Dictionary<string, VirtualSkeletonCandidate> _nodes = new Dictionary<string, VirtualSkeletonCandidate>();
    public VirtualSkeletonCandidateLedger(IEnumerable<VirtualSkeletonCandidate> nodes)
    {
        foreach (var node in nodes ?? Array.Empty<VirtualSkeletonCandidate>())
            if (node != null && !string.IsNullOrWhiteSpace(node.NodeId)) _nodes[node.NodeId] = node;
    }
    public bool TryGet(string nodeId, out VirtualSkeletonCandidate node) => _nodes.TryGetValue(nodeId ?? "", out node);
    public bool TryGetRuntimeNode(string nodeId, out VirtualSkeletonCandidate node)
    { return TryGet(nodeId, out node) && node.IsRuntimeEligible; }
    public int Count => _nodes.Count;

    public static VirtualSkeletonCandidateLedger CreateCurrentEvidenceLedger() => new VirtualSkeletonCandidateLedger(new[]
    {
        new VirtualSkeletonCandidate { NodeId="root.desktop-motion", EvidenceId="walk-baseline-2026-09-16", ModelEvidenceVersion="current-model", Status=SkeletonEvidenceStatus.Conditional, Resources=EmbodiedResource.Movement },
        new VirtualSkeletonCandidate { NodeId="head.orientation", ParentNodeId="root.desktop-motion", EvidenceId="ParamAngleX-dynamic-2026-09-16", ModelEvidenceVersion="current-model", Status=SkeletonEvidenceStatus.Supporting, Resources=EmbodiedResource.Face },
        new VirtualSkeletonCandidate { NodeId="torso.physics-lean", ParentNodeId="root.desktop-motion", EvidenceId="ParamBodyAngleZ-cropped-dual-2026-09-17", ModelEvidenceVersion="current-model", Status=SkeletonEvidenceStatus.Supporting, Resources=EmbodiedResource.Body },
        new VirtualSkeletonCandidate { NodeId="arm.screen-side-raise", ParentNodeId="torso.physics-lean", EvidenceId="Param94-dynamic-2026-09-16", ModelEvidenceVersion="current-model", Status=SkeletonEvidenceStatus.Supporting, Resources=EmbodiedResource.RightArm }
    });
}
