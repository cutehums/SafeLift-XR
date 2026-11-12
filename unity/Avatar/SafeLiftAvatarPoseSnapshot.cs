using System;
using UnityEngine;

[Serializable]
public sealed class SafeLiftAvatarPoseSnapshot
{
    public string phase;
    public string sourceAvatarName;
    public double timestamp;
    public float qualityScore;
    public SafeLiftAvatarBonePose[] bones = Array.Empty<SafeLiftAvatarBonePose>();

    public bool HasBones => bones != null && bones.Length > 0;
}

[Serializable]
public sealed class SafeLiftAvatarBonePose
{
    public string path;
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
}
