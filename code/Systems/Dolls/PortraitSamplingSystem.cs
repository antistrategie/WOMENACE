using Il2CppMenace.Conversations;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

public sealed class PortraitSamplingSystem : JiangyuSystem
{
    private readonly Dictionary<int, Sampling> _original = [];

    private sealed class Sampling(Texture2D texture)
    {
        public readonly Texture2D Texture = texture;
        public readonly FilterMode Filter = texture.filterMode;
        public readonly float Bias = texture.mipMapBias;
    }

    public override void OnTemplatesApplied()
    {
        foreach (var speaker in Templates.All<SpeakerTemplate>())
        {
            if (Affinity.ParseCharacterTag(speaker.Tags) == null)
                continue;
            Apply(speaker.StandLookLeftImage);
            Apply(speaker.StandLookRightImage);
            Apply(speaker.StandLookRightInactiveImage);
        }
    }

    private void Apply(Texture2D texture)
    {
        if (texture == null || texture.mipmapCount <= 1)
            return;
        _original.TryAdd(texture.GetInstanceID(), new Sampling(texture));
        // These shared UI portraits are mipmapped. Blend between levels as the UI scales,
        // favouring a little more detail than the default bilinear selection.
        texture.filterMode = FilterMode.Trilinear;
        texture.mipMapBias = -.5f;
    }

    public override void OnUnload()
    {
        foreach (var original in _original.Values)
        {
            if (original.Texture == null)
                continue;
            original.Texture.filterMode = original.Filter;
            original.Texture.mipMapBias = original.Bias;
        }
        _original.Clear();
    }
}
