using System.Globalization;
using System.Text;
using UnityEngine;

namespace WOMENACE.Code;

// Frame-by-frame flight recorder for tuning: a ring buffer of the last few
// thousand frames of every chain, with the frame timing and the head's motion
// beside it, dumped to CSV on request. A twitch is a handful of frames, so the
// player plays until one happens and the dump holds the frames around it.
// Nothing is allocated until recording starts.
internal sealed partial class SpringBoneRig
{
    private const int FrameColumns = 19;
    private const int BoneColumns = 7;

    private static string[] HumanoidBones => SpringChainBuilder.HumanoidBones;

    private Transform[] _humanoid;
    private const int ChainColumns = 9;

    private Transform _head;
    private Quaternion _lastHeadRotation;
    private float[] _record;
    private int _recordCapacity;
    private int _recordWidth;
    private int _recordNext;
    private int _recordCount;
    private int _recordFrame;

    internal void StartRecording(int frames)
    {
        // Hits counts collider pushes per frame, so the first row starts
        // from none.
        foreach (var rc in _chains)
            rc.Chain.Hits = 0;
        _recordCapacity = Mathf.Max(60, frames);
        _recordWidth = FrameColumns + BoneColumns * HumanoidBones.Length + ChainColumns * _chains.Length;
        _record = new float[_recordCapacity * _recordWidth];
        _recordNext = 0;
        _recordCount = 0;
        _recordFrame = 0;
        if (_head != null)
            _lastHeadRotation = _head.rotation;
    }

    internal bool Recording => _record != null;

    private void Record(float deltaTime, int steps)
    {
        if (_record == null)
            return;
        var o = _recordNext * _recordWidth;
        var root = Root.position;
        var headSpeed = 0f;
        var headPosition = Vector3.zero;
        var headRotation = Quaternion.identity;
        if (_head != null)
        {
            headRotation = _head.rotation;
            headSpeed = deltaTime > 0f ? Quaternion.Angle(_lastHeadRotation, headRotation) / deltaTime : 0f;
            _lastHeadRotation = headRotation;
            headPosition = _head.position;
        }
        _record[o++] = _recordFrame++;
        _record[o++] = Time.realtimeSinceStartup;
        _record[o++] = deltaTime;
        _record[o++] = steps;
        _record[o++] = root.x;
        _record[o++] = root.y;
        _record[o++] = root.z;
        _record[o++] = headSpeed;
        _record[o++] = headPosition.x;
        _record[o++] = headPosition.y;
        _record[o++] = headPosition.z;
        _record[o++] = headRotation.x;
        _record[o++] = headRotation.y;
        _record[o++] = headRotation.z;
        _record[o++] = headRotation.w;
        // The root's rotation, then each humanoid bone in the root's space.
        var rootRotation = N(Root.rotation);
        var inverseRoot = System.Numerics.Quaternion.Inverse(rootRotation);
        var rootPosition = N(root);
        _record[o++] = rootRotation.X;
        _record[o++] = rootRotation.Y;
        _record[o++] = rootRotation.Z;
        _record[o++] = rootRotation.W;
        foreach (var bone in _humanoid)
        {
            if (bone == null)
            {
                o += BoneColumns;
                continue;
            }
            var position = System.Numerics.Vector3.Transform(N(bone.position) - rootPosition, inverseRoot);
            var rotation = inverseRoot * N(bone.rotation);
            _record[o++] = position.X;
            _record[o++] = position.Y;
            _record[o++] = position.Z;
            _record[o++] = rotation.X;
            _record[o++] = rotation.Y;
            _record[o++] = rotation.Z;
            _record[o++] = rotation.W;
        }
        foreach (var rc in _chains)
        {
            var chain = rc.Chain;
            var rootBend = SpringSolver.AngleDegrees(N(rc.Bones[0].localRotation), chain.Joints[0].RestLocalRotation);
            var maxBend = 0f;
            for (var i = 1; i < chain.Joints.Length; i++)
                maxBend = Math.Max(maxBend, SpringSolver.AngleDegrees(N(rc.Bones[i].localRotation), chain.Joints[i].RestLocalRotation));
            var tipWorld = rc.Tip.position;
            var tipLocal = rc.ParentTransform.InverseTransformPoint(tipWorld);
            _record[o++] = rootBend;
            _record[o++] = maxBend;
            _record[o++] = tipLocal.x;
            _record[o++] = tipLocal.y;
            _record[o++] = tipLocal.z;
            _record[o++] = tipWorld.x - headPosition.x;
            _record[o++] = tipWorld.y - headPosition.y;
            _record[o++] = tipWorld.z - headPosition.z;
            _record[o++] = chain.Hits;
            chain.Hits = 0;
        }
        _recordNext = (_recordNext + 1) % _recordCapacity;
        _recordCount = Mathf.Min(_recordCount + 1, _recordCapacity);
    }

    // Writes the buffer oldest first and returns the row count. Tip
    // positions are in metres: local is in the chain root's parent space,
    // world is relative to the head so travel across the map drops out. Hits
    // counts the particles pushed out of a collider over the frame's steps.
    // Humanoid bones are in the root's space: position, then rotation.
    internal int DumpRecording(string path)
    {
        if (_record == null)
            return 0;
        var sb = new StringBuilder();
        sb.Append("frame,time,dt,steps,root_x,root_y,root_z,head_deg_per_s,head_x,head_y,head_z,head_qx,head_qy,head_qz,head_qw,root_qx,root_qy,root_qz,root_qw");
        foreach (var name in HumanoidBones)
            sb.Append($",{name}_px,{name}_py,{name}_pz,{name}_qx,{name}_qy,{name}_qz,{name}_qw");
        foreach (var rc in _chains)
        {
            var n = rc.Chain.Root;
            sb.Append($",{n}_root_bend,{n}_max_bend,{n}_tip_lx,{n}_tip_ly,{n}_tip_lz,{n}_tip_wx,{n}_tip_wy,{n}_tip_wz,{n}_hits");
        }
        sb.Append('\n');
        var start = _recordCount < _recordCapacity ? 0 : _recordNext;
        for (var r = 0; r < _recordCount; r++)
        {
            var o = ((start + r) % _recordCapacity) * _recordWidth;
            for (var c = 0; c < _recordWidth; c++)
            {
                if (c > 0)
                    sb.Append(',');
                sb.Append(_record[o + c].ToString("G7", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
        }
        System.IO.File.WriteAllText(path, sb.ToString());
        return _recordCount;
    }
}
