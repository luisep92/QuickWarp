using System;
using UnityEngine;

namespace QuickWarp.Domain;

[Serializable]
public sealed class WarpData
{
    public string Scene = string.Empty;

    public Vector3 Position;

    public Vector2 Velocity;
}
